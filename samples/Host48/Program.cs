using System;
using System.Linq;
using System.Windows.Forms;
using Avalonia;
using Avalonia.Win32.Interoperability;
using SIL.TextEditing.Playground;
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
        /// <summary>FieldWorks' shape: a WinForms form, the playground embedded between two WinForms TextBoxes, to try Tab
        /// and key handoff and focus. Options: <c>--scenario "name"</c>, <c>--minimized</c>.</summary>
        [STAThread]
        static void Main(string[] args)
        {
            AppBuilder.Configure<PlaygroundApp>().UsePlatformDetect().LogToTrace().SetupWithoutStarting();
            int s = Array.IndexOf(args, "--scenario");
            var view = s >= 0 && s + 1 < args.Length ? new PlaygroundView(args[s + 1]) : new PlaygroundView();
            var form = new Form
            {
                Text = "SIL.TextEditing playground in WinForms (net48, Avalonia " + PlaygroundApp.AvaloniaVersion + ")",
                ClientSize = new System.Drawing.Size(1100, 720),
            };
            var before = new TextBox { Dock = DockStyle.Top, Text = "WinForms TextBox (before)", TabIndex = 0 };
            var after = new TextBox { Dock = DockStyle.Bottom, Text = "WinForms TextBox (after)", TabIndex = 2 };
            var host = new KeyClaimingHost { Dock = DockStyle.Fill, TabStop = true, TabIndex = 1, Content = view };
            form.Controls.Add(host);
            form.Controls.Add(after);
            form.Controls.Add(before);
            if (args.Contains("--minimized")) form.WindowState = FormWindowState.Minimized;
            System.Windows.Forms.Application.Run(form);
        }
    }
}
