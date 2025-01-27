using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.Input.HLE;
using Ryujinx.SDL3.Common;
using System;
using System.Runtime.InteropServices;
using static SDL3.SDL;

namespace Ryujinx.Headless
{
    class VulkanWindow : WindowBase
    {
        public VulkanWindow(
            InputManager inputManager,
            GraphicsDebugLevel glLogLevel,
            AspectRatio aspectRatio,
            bool enableMouse,
            HideCursorMode hideCursorMode,
            bool ignoreControllerApplet)
            : base(inputManager, glLogLevel, aspectRatio, enableMouse, hideCursorMode, ignoreControllerApplet)
        {
        }

        public override SDL_WindowFlags WindowFlags => SDL_WindowFlags.SDL_WINDOW_VULKAN;

        protected override void InitializeWindowRenderer() { }

        protected override void InitializeRenderer()
        {
            if (IsExclusiveFullscreen)
            {
                Renderer?.Window.SetSize(ExclusiveFullscreenWidth, ExclusiveFullscreenHeight);
                MouseDriver.SetClientSize(ExclusiveFullscreenWidth, ExclusiveFullscreenHeight);
            }
            else
            {
                Renderer?.Window.SetSize(DefaultWidth, DefaultHeight);
                MouseDriver.SetClientSize(DefaultWidth, DefaultHeight);
            }
        }

        private static void BasicInvoke(Action action)
        {
            action();
        }

        public nint CreateWindowSurface(nint instance)
        {
            ulong surfaceHandle = 0;

            void CreateSurface()
            {
                if (!SDL_Vulkan_CreateSurface(WindowHandle, instance, IntPtr.Zero, out surfaceHandle))
                {
                    string errorMessage = $"SDL_Vulkan_CreateSurface failed with error \"{SDL_GetError()}\"";

                    Logger.Error?.Print(LogClass.Application, errorMessage);

                    throw new Exception(errorMessage);
                }
            }

            if (SDL3Driver.MainThreadDispatcher != null)
            {
                SDL3Driver.MainThreadDispatcher(CreateSurface);
            }
            else
            {
                CreateSurface();
            }

            return (nint)surfaceHandle;
        }

        public unsafe string[] GetRequiredInstanceExtensions()
        {
            nint rawExtensions = SDL_Vulkan_GetInstanceExtensions(out uint count);
            IntPtr[] extensionPointers = new IntPtr[count];

            Marshal.Copy(rawExtensions, extensionPointers, 0, (int)count);
            if (rawExtensions != nint.Zero)
            {
                string[] extensions = new string[(int)count];
                for (int i = 0; i < extensions.Length; i++)
                {
                    extensions[i] = Marshal.PtrToStringUTF8(extensionPointers[i]);
                }

                return extensions;
            }

            string errorMessage = $"SDL_Vulkan_GetInstanceExtensions failed with error \"{SDL_GetError()}\"";

            Logger.Error?.Print(LogClass.Application, errorMessage);

            throw new Exception(errorMessage);
        }

        protected override void FinalizeWindowRenderer()
        {
            Device.DisposeGpu();
        }

        protected override void SwapBuffers() { }
    }
}
