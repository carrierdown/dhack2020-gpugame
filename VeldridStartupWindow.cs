using System;
using System.Diagnostics;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using Veldrid.Utilities;

namespace DinoDips
{
    public class VeldridStartupWindow : IApplicationWindow
    {
        private readonly Sdl2Window Window;
        private GraphicsDevice GD;
        private DisposeCollectorResourceFactory Factory;
        private bool WindowResized = true;

        public event Action<float, long> Rendering;
        public event Action<GraphicsDevice, ResourceFactory, Swapchain> GraphicsDeviceCreated;
        public event Action GraphicsDeviceDestroyed;
        public event Action Resized;
        public event Action<KeyEvent> KeyPressed;

        public uint Width => (uint)Window.Width;
        public uint Height => (uint)Window.Height;

        public VeldridStartupWindow(string title)
        {
            WindowCreateInfo wci = new WindowCreateInfo
            {
                X = 450,
                Y = 200,
                WindowWidth = 1280,
                WindowHeight = 720,
                WindowTitle = title,
            };
            Window = VeldridStartup.CreateWindow(ref wci);
            Window.Resized += () =>
            {
                WindowResized = true;
            };
            Window.KeyDown += OnKeyDown;
        }

        public void Run()
        {
            GraphicsDeviceOptions options = new GraphicsDeviceOptions(
                debug: false,
                swapchainDepthFormat: PixelFormat.R16_UNorm,
                syncToVerticalBlank: true,
                resourceBindingModel: ResourceBindingModel.Improved,
                preferDepthRangeZeroToOne: true,
                preferStandardClipSpaceYDirection: true);
#if DEBUG
            options.Debug = true;
#endif
            GD = VeldridStartup.CreateGraphicsDevice(Window, options, GraphicsBackend.Direct3D11);
            Factory = new DisposeCollectorResourceFactory(GD.ResourceFactory);
            GraphicsDeviceCreated?.Invoke(GD, Factory, GD.MainSwapchain);

            Stopwatch sw = Stopwatch.StartNew();
            double previousElapsed = sw.Elapsed.TotalSeconds;
            long ticks = 0;

            while (Window.Exists)
            {
                double newElapsed = sw.Elapsed.TotalSeconds;
                float deltaSeconds = (float)(newElapsed - previousElapsed);

                InputSnapshot inputSnapshot = Window.PumpEvents();
                InputTracker.UpdateFrameInput(inputSnapshot);

                if (Window.Exists)
                {
                    previousElapsed = newElapsed;
                    if (WindowResized)
                    {
                        WindowResized = false;
                        GD.ResizeMainWindow((uint)Window.Width, (uint)Window.Height);
                        Resized?.Invoke();
                    }

                    Rendering?.Invoke(deltaSeconds, ticks++);
                }
            }

            GD.WaitForIdle();
            Factory.DisposeCollector.DisposeAll();
            GD.Dispose();
            GraphicsDeviceDestroyed?.Invoke();
        }

        protected void OnKeyDown(KeyEvent keyEvent)
        {
            KeyPressed?.Invoke(keyEvent);
        }
    }
}
