using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Controls;

namespace SharpShell.SharpPreviewHandler
{
    /// <summary>
    /// Interaction logic for PreviewHandlerControlWpf.xaml
    /// </summary>
    public class PreviewHandlerControl : UserControl
    {
        /// <summary>
        /// Sets the color of the background, if possible, to coordinate with the windows
        /// color scheme.
        /// </summary>
        /// <param name="color">The color.</param>
        protected internal virtual void SetVisualsBackgroundColor(System.Drawing.Color color) { }

        /// <summary>
        /// Sets the color of the text, if possible, to coordinate with the windows
        /// color scheme.
        /// </summary>
        /// <param name="color">The color.</param>
        protected internal virtual void SetVisualsTextColor(System.Drawing.Color color) { }

        /// <summary>
        /// Sets the font, if possible, to coordinate with the windows
        /// color scheme.
        /// </summary>
        /// <param name="font">The font.</param>
        protected internal virtual void SetVisualsFont(System.Drawing.Font font) { }
    }
}
