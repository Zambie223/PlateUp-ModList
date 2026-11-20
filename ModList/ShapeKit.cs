using Shapes;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace ModList
{
    /// <summary>Where runtime-created shapes draw: just above a vanilla element's label.</summary>
    internal struct ShapeLayer
    {
        public int Layer;
        public int SortingLayer;
        public int SortingOrder;
        public CompareFunction ZTest;
        public ShapesBlendMode Blend;

        /// <summary>The same layer, <paramref name="by"/> sorting orders higher.</summary>
        public ShapeLayer Raised(int by)
        {
            ShapeLayer raised = this;
            raised.SortingOrder += by;
            return raised;
        }
    }

    /// <summary>
    /// Small helpers for drawing with Shapes, the vector library vanilla's buttons and pickers are
    /// drawn with. All of Shapes' shaders ship in the game's resources.assets, so shapes created
    /// at runtime render. Callers wrap these in try / catch and fall back to plain text.
    /// </summary>
    internal static class ShapeKit
    {
        /// <summary>
        /// Above <paramref name="label"/> (which already draws over <paramref name="backing"/>),
        /// on the label's sorting layer, with the backing's depth test and blending.
        /// </summary>
        public static ShapeLayer Above(Rectangle backing, TextMeshPro label)
        {
            MeshRenderer renderer = label.GetComponent<MeshRenderer>();
            return new ShapeLayer
            {
                Layer = label.gameObject.layer,
                SortingLayer = renderer != null ? renderer.sortingLayerID : backing.SortingLayerID,
                SortingOrder = (renderer != null ? renderer.sortingOrder : backing.SortingOrder) + 1,
                ZTest = backing.ZTest,
                Blend = backing.BlendMode,
            };
        }

        /// <summary>The label's depth in <paramref name="space"/>'s local coordinates.</summary>
        public static float LocalZ(Transform space, TextMeshPro label)
        {
            return space.InverseTransformPoint(label.transform.position).z;
        }

        public static GameObject Group(Transform parent, string name, Vector3 position, ShapeLayer layer)
        {
            GameObject go = new GameObject(name);
            go.layer = layer.Layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go;
        }

        public static T Add<T>(Transform parent, string name, Vector3 position, float angle, ShapeLayer layer, Color colour)
            where T : ShapeRenderer
        {
            GameObject go = Group(parent, name, position, layer);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            T shape = go.AddComponent<T>();
            shape.BlendMode = layer.Blend;
            shape.ZTest = layer.ZTest;
            shape.SortingLayerID = layer.SortingLayer;
            shape.SortingOrder = layer.SortingOrder;
            shape.Color = colour;
            return shape;
        }
    }
}
