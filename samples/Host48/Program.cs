using System;
using System.Windows.Forms;
using Avalonia;
using Avalonia.Win32.Interoperability;
using SIL.TextEditing.Samples;

namespace SIL.TextEditing.Samples.Host48
{
    /// <summary>The usual way to host Avalonia in WinForms: a control host that claims the arrow keys while the
    /// Avalonia content has focus, so the form does not use them for navigation.</summary>
    sealed class KeyClaimingHost : WinFormsAvaloniaControlHost
    {
        static bool IsArrow(Keys k)
        {
            switch (k & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                    return true;
                default:
                    return false;
            }
        }

        protected override bool IsInputKey(Keys keyData) => (IsArrow(keyData) && ContainsFocus) || base.IsInputKey(keyData);

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => !(IsArrow(keyData) && ContainsFocus) && base.ProcessCmdKey(ref msg, keyData);
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            AppBuilder.Configure<SampleApp>().UsePlatformDetect().LogToTrace().SetupWithoutStarting();
            var ui = new SampleUi();
            var form = new Form { Text = "SIL.TextEditing sample (net48, Avalonia 11.3.17)", ClientSize = new System.Drawing.Size(520, 420) };
            var before = new TextBox { Dock = DockStyle.Top, Text = "WinForms TextBox (before)" };
            var after = new TextBox { Dock = DockStyle.Bottom, Text = "WinForms TextBox (after)" };
            var host = new KeyClaimingHost { Dock = DockStyle.Fill, TabStop = true, Content = ui.Root };
            form.Controls.Add(host);
            form.Controls.Add(after);
            form.Controls.Add(before);
            System.Windows.Forms.Application.Run(form);
        }
    }
}
