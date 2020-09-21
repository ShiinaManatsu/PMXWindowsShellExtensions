using SharpShell.Attributes;
using SharpShell.SharpPreviewHandler;
using System;
using System.Runtime.InteropServices;

namespace Preview.PMX
{
    /// <summary>
    /// The IconPreviewHandler is a preview handler that shows the icons contained
    /// in an ico file.
    /// </summary>
    [ComVisible(true)]
    [COMServerAssociation(AssociationType.ClassOfExtension, ".ico")]
    [DisplayName("Pmx Preview Handler")]
    [Guid("2008934A-C718-43D3-8AF0-22516DB63FD1")]
    public class PmxPreviewHandler : SharpPreviewHandler
    {
        /// <summary>
        /// DoPreview must create the preview handler user interface and initialize it with data
        /// provided by the shell.
        /// </summary>
        /// <returns>
        /// The preview handler user interface.
        /// </returns>
        protected override PreviewHandlerControl DoPreview()
        {
            //  Create the handler control.
            var handler = new ViewPortWindow();
            
            //  Do we have a file path? If so, we can do a preview.
            if (!string.IsNullOrEmpty(SelectedFilePath))
                handler.DoPreview(SelectedFilePath);

            //  Return the handler control.
            return handler;
        }
    }
}
