using SharpShell.Attributes;
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Thumbnail.PMX
{
    /// <summary>
    /// The PMXThumbnailHandler is a ThumbnailHandler for pmx files
    /// </summary>
    [ComVisible(true)]
    [Guid("88615FC3-2F4A-463B-805B-1ED5BFF4F393")]
    [COMServerAssociation(AssociationType.FileExtension, ".pmx")]
    public class PMXThumbnailHandler : FileThumbnailHandler
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PMXThumbnailHandler"/> class
        /// </summary>
        public PMXThumbnailHandler()
        {
            //  Create our lazy objects
        }

        /// <summary>
        /// Gets the thumbnail image
        /// </summary>
        /// <param name="width">The width of the image that should be returned.</param>
        /// <returns>
        /// The image for the thumbnail
        /// </returns>
        protected override Bitmap GetThumbnailImage(uint width)
        {
            Bitmap bitmap;
            try
            {
                bitmap = new PMXRenderer.PMXRenderer().GeneratePmxPreview(SelectedItemPath, (int)width, (int)width);
            }
            finally
            {
                GC.Collect();
            }

            return bitmap;
        }
    }

}