using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ECM_Stage_Helper_Tool
{
    /// <summary>
    /// Unterdrückt alle WM_PAINT-Nachrichten eines Controls für die Dauer des using-Blocks.
    /// Am Ende wird WM_SETREDRAW wieder aktiviert und ein einziges vollständiges Refresh ausgelöst.
    /// Reduziert das Neuzeichnen bei vielen Zell-Änderungen von N auf 1.
    /// </summary>
    internal sealed class DrawingLocker : IDisposable
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_SETREDRAW = 0x000B;

        private readonly Control _control;

        public DrawingLocker(Control control)
        {
            _control = control;
            SendMessage(_control.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero); // Zeichnen aus
        }

        public void Dispose()
        {
            SendMessage(_control.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero); // Zeichnen an
            _control.Invalidate(true);
            _control.Update();
        }
    }
}
