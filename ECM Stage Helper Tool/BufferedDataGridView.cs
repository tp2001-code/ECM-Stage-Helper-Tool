using System.Windows.Forms;

namespace ECM_Stage_Helper_Tool
{
    /// <summary>
    /// DataGridView mit aktiviertem OptimisticDoubleBuffer-Flag.
    /// Verhindert das zeilenweise Neuzeichnen beim Befüllen und eliminiert Flackern.
    /// </summary>
    internal sealed class BufferedDataGridView : DataGridView
    {
        public BufferedDataGridView()
        {
            DoubleBuffered = true;
            // Setzt intern ControlStyles.OptimizedDoubleBuffer + AllPaintingInWmPaint
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint,
                true);
        }
    }
}
