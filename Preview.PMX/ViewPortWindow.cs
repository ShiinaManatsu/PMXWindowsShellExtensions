using HelixToolkit.Wpf;
using MMDExtensions.PMX;
using SharpShell.SharpPreviewHandler;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Preview.PMX
{
    public class ViewPortWindow : PreviewHandlerControl
    {
        public ModelVisual3D PreviewModel { get; }
        public HelixViewport3D ViewPort { get; }

        public ViewPortWindow()
        {
            PreviewModel = new ModelVisual3D();
            ViewPort = new HelixViewport3D()
            {
                ZoomExtentsWhenLoaded = true,
                ZoomAroundMouseDownPoint = true,
                ShowViewCube = false,
                CameraRotationMode = CameraRotationMode.Turnball,
                RotateGesture = new MouseGesture(MouseAction.LeftClick)
            };


            ViewPort.Children.Add(PreviewModel);

            var positionTextblock = new TextBlock()
            {
                Foreground = new SolidColorBrush(Colors.White)
            };
            var rotationTextblock = new TextBlock()
            {
                Foreground = new SolidColorBrush(Colors.White)
            };

            ViewPort.CameraChanged += (_, __) =>
            {
                positionTextblock.Text = $"Position X:{ViewPort.Camera.Position.X}\nPosition Y:{ViewPort.Camera.Position.Y}\nPosition Z:{ViewPort.Camera.Position.Z}";
                rotationTextblock.Text = $"LookDirection X:{ViewPort.Camera.LookDirection.X}\nLookDirection Y:{ViewPort.Camera.LookDirection.Y}\nLookDirection Z:{ViewPort.Camera.LookDirection.Z}";
            };

            var stack = new StackPanel();

            var textStack = new StackPanel();

            textStack.Children.Add(positionTextblock);
            textStack.Children.Add(rotationTextblock);

            stack.Children.Add(ViewPort);
            stack.Children.Add(textStack);

            AddChild(stack);
        }
        public void DoPreview(String path)
        {

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
