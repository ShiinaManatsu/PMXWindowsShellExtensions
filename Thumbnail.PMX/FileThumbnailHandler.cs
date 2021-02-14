using SharpShell;
using SharpShell.Attributes;
using SharpShell.Interop;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Thumbnail.PMX
{
    /// <summary>
    /// The SharpIconHandler is the base class for SharpShell servers that provide
    /// custom Thumbnail Image Handlers.
    /// </summary>
    [ServerType(ServerType.ShellItemThumbnailHandler)]
    public abstract class FileThumbnailHandler : InitializeWithFileServer, IThumbnailProvider
    {
        public int GetThumbnail(uint cx, out IntPtr phbmp, out WTS_ALPHATYPE pdwAlpha)
        {
            //  DebugLog this key event.
            Log($"GetThumbnail for item stream, width {cx}.");

            //  Set the out variables to default values.
            phbmp = IntPtr.Zero;
            pdwAlpha = WTS_ALPHATYPE.WTSAT_UNKNOWN;
            Bitmap thumbnailImage;

            try
            {
                //  Get the thumbnail image.
                thumbnailImage = GetThumbnailImage(cx);
            }
            catch (Exception exception)
            {
                //  DebugLog the exception and return failure.
                LogError("An exception occured when getting the thumbnail image.", exception);
                return WinError.E_FAIL;
            }

            //  If we couldn't get an image, return failure.
            if (thumbnailImage == null)
            {
                //  DebugLog a warning return failure.
                Log("The internal GetThumbnail function failed to return a valid thumbnail.");
                return WinError.E_FAIL;
            }

            //  Now we can set the image.
            phbmp = thumbnailImage.GetHbitmap();
            pdwAlpha = WTS_ALPHATYPE.WTSAT_ARGB;

            //  Return success.
            return WinError.S_OK;
        }

        /// <summary>
        /// Gets the thumbnail image for the currently selected item (SelectedItemStream).
        /// </summary>
        /// <param name="width">The width of the image that should be returned.</param>
        /// <returns>The image for the thumbnail.</returns>
        protected abstract Bitmap GetThumbnailImage(uint width);
    }
}
