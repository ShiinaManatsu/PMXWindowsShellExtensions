using HelixToolkit.Wpf;
using MMDExtensions;
using MMDExtensions.PMX;
using SharpShell.Attributes;
using SharpShell.SharpThumbnailHandler;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Thumbnail.PMX
{
    /// <summary>
    /// The PMXThumbnailHandler is a ThumbnailHandler for pmx files
    /// </summary>
    [ComVisible(true)]
    [Guid("88615FC3-2F4A-463B-805B-1ED5BFF4F393")]
    [COMServerAssociation(AssociationType.FileExtension, ".pmx")]
    public class PMXThumbnailHandler : SharpThumbnailHandler
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
            //  Attempt to open the stream with a reader
            try
            {
                using (var reader = new StreamReader(SelectedItemStream))
                {
                    var pmx = PMXLoaderScript.Import(reader.BaseStream);

                    MeshCreationInfo creation_info = CreateMeshCreationInfoSingle(pmx);

                    var models = new Model3DGroup();
                    for (int i = 0, i_max = creation_info.value.Length; i < i_max; ++i)
                    {
                        int[] indices = creation_info.value[i].plane_indices.Select(x => (int)creation_info.reassign_dictionary[x])
                                                                            .ToArray();
                        var mesh = new MeshGeometry3D
                        {
                            Positions = new Point3DCollection(pmx.vertex_list.vertex.Select(x => x.pos)),

                            TextureCoordinates = new PointCollection(pmx.vertex_list.vertex.Select(x => x.uv))
                        };

                        indices.ToList()
                            .ForEach(x => mesh.TriangleIndices.Add(x));
                        var material = new DiffuseMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 160, 160)));
                        var model = new GeometryModel3D(mesh, material)
                        {
                            BackMaterial = material
                        };

                        models.Children.Add(new GeometryModel3D(mesh, material)
                        {
                            BackMaterial = material
                        });
                    }

                    var visual = new ModelVisual3D
                    {
                        Content = models
                    };

                    var view = new HelixViewport3D();
                    view.Children.Add(visual);

                    view.Children.Add(new DefaultLights());
                    
                    try
                    {
                        var bitmap = view.Viewport.RenderBitmap(width, width, new SolidColorBrush(Colors.Transparent));

                        return BitmapFromSource(bitmap);
                    }
                    catch (Exception exception)
                    {
                        LogError("An exception occurred Rendering bitmap.", exception);
                        return null;
                    }

                }
            }
            catch (Exception exception)
            {
                //  Log the exception and return null for failure
                LogError("An exception occurred opening the text file.", exception);
                return null;
            }
        }
        private Bitmap BitmapFromSource(BitmapSource bitmapsource)
        {
            System.Drawing.Bitmap bitmap;
            using (MemoryStream outStream = new MemoryStream())
            {
                BitmapEncoder enc = new BmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bitmapsource));
                enc.Save(outStream);
                bitmap = new System.Drawing.Bitmap(outStream);
            }
            return bitmap;
        }

        private Bitmap Scale(Bitmap image, uint size)
        {
            var bmp = new Bitmap((int)size, (int)size);
            var graph = Graphics.FromImage(bmp);

            float scale = Math.Min(size / image.Width, size / image.Height);

            // uncomment for higher quality output
            //graph.InterpolationMode = InterpolationMode.High;
            //graph.CompositingQuality = CompositingQuality.HighQuality;
            //graph.SmoothingMode = SmoothingMode.AntiAlias;

            var scaleWidth = (int)(image.Width * scale);
            var scaleHeight = (int)(image.Height * scale);

            graph.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.Transparent), new RectangleF(0, 0, size, size));
            graph.DrawImage(image, ((int)size - scaleWidth) / 2, ((int)size - scaleHeight) / 2, scaleWidth, scaleHeight);

            return bmp;
        }

        MeshCreationInfo CreateMeshCreationInfoSingle(PMXFormat format)
        {
            MeshCreationInfo result = new MeshCreationInfo();
            //全マテリアルを設定
            result.value = CreateMeshCreationInfoPacks(format);
            //全頂点を設定
            result.all_vertices = Enumerable.Range(0, format.vertex_list.vertex.Length).Select(x => (uint)x).ToArray();
            //頂点リアサインインデックス用辞書作成
            result.reassign_dictionary = new Dictionary<uint, uint>(result.all_vertices.Length);
            for (uint i = 0, i_max = (uint)result.all_vertices.Length; i < i_max; ++i)
            {
                result.reassign_dictionary[i] = i;
            }
            return result;
        }
        MeshCreationInfo.Pack[] CreateMeshCreationInfoPacks(PMXFormat format)
        {
            uint plane_start = 0;
            //マテリアル単位のMeshCreationInfo.Packを作成する
            return Enumerable.Range(0, format.material_list.material.Length)
                            .Select(x =>
                            {
                                MeshCreationInfo.Pack pack = new MeshCreationInfo.Pack();
                                pack.material_index = (uint)x;
                                uint plane_count = format.material_list.material[x].face_vert_count;
                                pack.plane_indices = format.face_vertex_list.face_vert_index.Skip((int)plane_start)
                                                                                                        .Take((int)plane_count)
                                                                                                        .ToArray();
                                pack.vertices = pack.plane_indices.Distinct() //重複削除
                                                                        .ToArray();
                                plane_start += plane_count;
                                return pack;
                            })
                            .ToArray();
        }


        public class MeshCreationInfo
        {
            public class Pack
            {
                public uint material_index; //マテリアル
                public uint[] plane_indices;    //面
                public uint[] vertices;     //頂点
            }
            public Pack[] value;
            public uint[] all_vertices;         //総頂点
            public Dictionary<uint, uint> reassign_dictionary;  //頂点リアサインインデックス用辞書
        }
    }

}