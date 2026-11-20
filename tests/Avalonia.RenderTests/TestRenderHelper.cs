using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;
using Xunit;
using Avalonia.Input;
using Avalonia.Platform;
using System.Threading.Tasks;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using Avalonia.Platform.Surfaces;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.UnitTests;
using Avalonia.Utilities;
using Avalonia.Harfbuzz;
using Avalonia.OpenGL.Egl;
using Avalonia.Skia;
using SkiaSharp;

namespace Avalonia.Skia.RenderTests;

static class TestRenderHelper
{
    static TestRenderHelper()
    {
        SkiaPlatform.Initialize();
        AvaloniaLocator.CurrentMutable.Bind<IAssetLoader>().ToConstant(new StandardAssetLoader());
        AvaloniaLocator.CurrentMutable.Bind<ITextShaperImpl>().ToConstant(new HarfBuzzTextShaper());
        AvaloniaLocator.CurrentMutable.Bind<ICursorFactory>().ToConstant(new NullCursorFactory());
    }

    private sealed class NullCursorFactory : ICursorFactory
    {
        public ICursorImpl GetCursor(StandardCursorType cursorType) => new NullCursor();
        public ICursorImpl CreateCursor(Bitmap cursor, PixelPoint hotSpot) => new NullCursor();

        private sealed class NullCursor : ICursorImpl
        {
            public void Dispose() { }
        }
    }
    
    
    public static Task RenderToFile(Control target, string path, bool immediate, double dpi = 96)
    {
        if (immediate)
        {
            EnsureOutputDirectory(path);

            var pixelSize = new PixelSize((int)target.Width, (int)target.Height);
            var size = new Size(target.Width, target.Height);
            var dpiVector = new Vector(dpi, dpi);

            using (RenderTargetBitmap bitmap = new RenderTargetBitmap(pixelSize, dpiVector))
            {
                target.Measure(size);
                target.Arrange(new Rect(size));
                bitmap.Render(target);
                bitmap.Save(path, PngBitmapEncoderOptions.Default);
            }

            return Task.CompletedTask;
        }

        return RenderCompositedToFile(target, path, null, dpi);
    }

    public static Task RenderCompositedToFile(Control target, string path, IPlatformGraphics? gpu, double dpi = 96)
    {
        EnsureOutputDirectory(path);

        var factory = AvaloniaLocator.Current.GetRequiredService<IPlatformRenderInterface>();
        var pixelSize = new PixelSize((int)target.Width, (int)target.Height);
        var dpiVector = new Vector(dpi, dpi);
        var scaling = dpi / 96;

        var timer = new ManualRenderTimer();

        var compositor = new Compositor(RenderLoop.FromTimer(timer), gpu, true,
            new DispatcherCompositorScheduler(), true, Dispatcher.UIThread);
        // GPU readback surfaces produce RGBA
        var pixelFormat = gpu == null ? factory.DefaultPixelFormat : PixelFormats.Rgba8888;
        using (var writableBitmap = factory.CreateWriteableBitmap(pixelSize, dpiVector, pixelFormat,
                   factory.DefaultAlphaFormat))
        {
            try
            {
                IPlatformRenderSurface surface = gpu switch
                {
                    null => new BitmapFramebufferSurface(writableBitmap),
                    EglPlatformGraphics => new FboReadbackGlPlatformSurface(writableBitmap, scaling),
                    _ => new VulkanReadbackPlatformSurface(writableBitmap, scaling)
                };
                var root = new TestRenderRoot(scaling, null!);
                try
                {
                    using (var renderer = new CompositingRenderer(root, compositor, () => new[] { surface }))
                    {
                        root.Initialize(renderer, target);
                        renderer.Start();
                        Dispatcher.UIThread.RunJobs();
                        renderer.Paint(new Rect(root.Bounds.Size), false);
                    }
                }
                finally
                {
                    // Detach the control, so it can be rendered again with a different root
                    root.Child = null;
                }
            }
            finally
            {
                if (gpu != null)
                    DisposeGpuBackendContext(compositor, gpu);
            }

            using var fileStream = File.Create(path);
            writableBitmap.Save(fileStream, PngBitmapEncoderOptions.Default);
        }

        return Task.CompletedTask;
    }

    // Compositors are created per render, so their backend contexts (GRContext etc.) have to be
    // disposed deterministically instead of piling up until finalization
    private static void DisposeGpuBackendContext(Compositor compositor, IPlatformGraphics gpu)
    {
        var renderInterface = compositor.Server.RenderInterface;
        using (renderInterface.EnsureCurrent())
            // TODO: Technically it should be disposing the GPU context as well,
            // but need to re-check wayland code if it's safe to do
            compositor.Server.ResetAllGpuResources();
        if (!gpu.UsesSharedContext)
            renderInterface.GpuContext?.Dispose();
    }

    private static void EnsureOutputDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        Assert.NotNull(dir);

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    class BitmapFramebufferSurface : IFramebufferPlatformSurface
    {
        private readonly IWriteableBitmapImpl _bitmap;

        public BitmapFramebufferSurface(IWriteableBitmapImpl bitmap)
        {
            _bitmap = bitmap;
        }

        public IFramebufferRenderTarget CreateFramebufferRenderTarget()
        {
            return new FuncFramebufferRenderTarget(() => _bitmap.Lock());
        }
    }


    public static void BeginTest()
    {
        Dispatcher.ResetBeforeUnitTests();
    }

    public static void EndTest()
    {
        if (Dispatcher.UIThread.CheckAccess()) 
            Dispatcher.UIThread.RunJobs();
        Dispatcher.ResetForUnitTests();
    }
    
    public static string GetTestsDirectory()
    {
        var path = Directory.GetCurrentDirectory();

        while (!string.IsNullOrEmpty(path) && Path.GetFileName(path) != "tests")
        {
            path = Path.GetDirectoryName(path);
        }

        Assert.NotNull(path);
        return path;
    }

    public static void AssertCompareImages(string actualPath, string expectedPath)
    {
        using (var expected = SKBitmap.Decode(expectedPath) ?? throw new InvalidDataException($"Could not decode {expectedPath}."))
        using (var actual = SKBitmap.Decode(actualPath) ?? throw new InvalidDataException($"Could not decode {actualPath}."))
        {
            double immediateError = TestRenderHelper.CompareImages(actual, expected);

            if (immediateError > 0.022)
            {
                Assert.Fail(actualPath + ": Error = " + immediateError);
            }
        }
    }
    
    /// <summary>
    /// Calculates root mean square error for given two images.
    /// Based roughly on ImageMagick implementation to ensure consistency.
    /// </summary>
    public static double CompareImages(SKBitmap actual, SKBitmap expected)
    {
        if (actual.Width != expected.Width || actual.Height != expected.Height)
        {
            throw new ArgumentException("Images have different resolutions");
        }

        var quantity = actual.Width * actual.Height;
        double squaresError = 0;

        const double scale = 1 / 255d;
            
        for (var x = 0; x < actual.Width; x++)
        {
            double localError = 0;
                
            for (var y = 0; y < actual.Height; y++)
            {
                var expectedColor = expected.GetPixel(x, y);
                var actualColor = actual.GetPixel(x, y);
                var expectedAlpha = expectedColor.Alpha * scale;
                var actualAlpha = actualColor.Alpha * scale;
                    
                var r = scale * (expectedAlpha * expectedColor.Red - actualAlpha * actualColor.Red);
                var g = scale * (expectedAlpha * expectedColor.Green - actualAlpha * actualColor.Green);
                var b = scale * (expectedAlpha * expectedColor.Blue - actualAlpha * actualColor.Blue);
                var a = expectedAlpha - actualAlpha;

                var error = r * r + g * g + b * b + a * a;

                localError += error;
            }

            squaresError += localError;
        }

        var meanSquaresError = squaresError / quantity;

        const int channelCount = 4;
            
        meanSquaresError = meanSquaresError / channelCount;
            
        return Math.Sqrt(meanSquaresError);
    }

}
