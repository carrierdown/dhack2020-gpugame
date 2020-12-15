using System.Collections.Generic;
using System.Numerics;
using Veldrid;

namespace DinoDips
{
    public static class InputTracker
    {
        private static HashSet<Key> CurrentlyPressedKeys = new HashSet<Key>();
        private static HashSet<Key> NewKeysThisFrame = new HashSet<Key>();

        private static HashSet<MouseButton> CurrentlyPressedMouseButtons = new HashSet<MouseButton>();
        private static HashSet<MouseButton> NewMouseButtonsThisFrame = new HashSet<MouseButton>();

        public static Vector2 MousePosition;
        public static InputSnapshot FrameSnapshot { get; private set; }

        public static bool GetKey(Key key)
        {
            return CurrentlyPressedKeys.Contains(key);
        }

        public static bool GetKeyDown(Key key)
        {
            return NewKeysThisFrame.Contains(key);
        }

        public static bool GetMouseButton(MouseButton button)
        {
            return CurrentlyPressedMouseButtons.Contains(button);
        }

        public static bool GetMouseButtonDown(MouseButton button)
        {
            return NewMouseButtonsThisFrame.Contains(button);
        }

        public static void UpdateFrameInput(InputSnapshot snapshot)
        {
            FrameSnapshot = snapshot;
            NewKeysThisFrame.Clear();
            NewMouseButtonsThisFrame.Clear();

            MousePosition = snapshot.MousePosition;
            for (int i = 0; i < snapshot.KeyEvents.Count; i++)
            {
                KeyEvent ke = snapshot.KeyEvents[i];
                if (ke.Down)
                {
                    KeyDown(ke.Key);
                }
                else
                {
                    KeyUp(ke.Key);
                }
            }
            for (int i = 0; i < snapshot.MouseEvents.Count; i++)
            {
                MouseEvent me = snapshot.MouseEvents[i];
                if (me.Down)
                {
                    MouseDown(me.MouseButton);
                }
                else
                {
                    MouseUp(me.MouseButton);
                }
            }
        }

        private static void MouseUp(MouseButton mouseButton)
        {
            CurrentlyPressedMouseButtons.Remove(mouseButton);
            NewMouseButtonsThisFrame.Remove(mouseButton);
        }

        private static void MouseDown(MouseButton mouseButton)
        {
            if (CurrentlyPressedMouseButtons.Add(mouseButton))
            {
                NewMouseButtonsThisFrame.Add(mouseButton);
            }
        }

        private static void KeyUp(Key key)
        {
            CurrentlyPressedKeys.Remove(key);
            NewKeysThisFrame.Remove(key);
        }

        private static void KeyDown(Key key)
        {
            if (CurrentlyPressedKeys.Add(key))
            {
                NewKeysThisFrame.Add(key);
            }
        }
    }
}
