﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.Robotics.MessageVisualizers
{
    public class BasicDrawing: MonoBehaviour
    {
        static GUIStyle s_LabelStyle;

        struct LabelInfo3D
        {
            public Vector3 position;
            public string text;
            public Color color;
            public float worldSpacing;
        }

        Mesh m_Mesh;
        List<Vector3> m_Vertices = new List<Vector3>();
        List<Color32> m_Colors32 = new List<Color32>();
        List<GameObject> m_Supplemental = new List<GameObject>();
        List<int> m_Triangles = new List<int>();
        List<LabelInfo3D> m_Labels = new List<LabelInfo3D>();
        bool m_isDirty = false;

        public static BasicDrawing Create(float duration = -1, Material material = null)
        {
            return BasicDrawingManager.CreateDrawing(duration, material);
        }

        public void Init(BasicDrawingManager parent, Material material, float duration = -1)
        {
            m_Mesh = new Mesh();

            transform.parent = parent.transform;

            MeshFilter mfilter = gameObject.AddComponent<MeshFilter>();
            mfilter.sharedMesh = m_Mesh;
            MeshRenderer mrenderer = gameObject.AddComponent<MeshRenderer>();
            mrenderer.sharedMaterial = material;

            if(duration >= 0)
            {
                StartCoroutine(DestroyAfterDelay(duration));
            }

            if (s_LabelStyle == null)
            {
                s_LabelStyle = new GUIStyle()
                {
                    alignment = TextAnchor.LowerLeft,
                    wordWrap = false,
                };
            }
        }

        public IEnumerator DestroyAfterDelay(float duration)
        {
            yield return new WaitForSeconds(duration);
            Destroy();
        }

        public void DrawLine(Vector3 from, Vector3 to, Color32 color, float thickness = 0.1f)
        {
            Vector3 forwardVector = (to - from).normalized;
            Vector3 sideVector;
            if (Vector3.Dot(forwardVector, Vector3.up) > 0.9f) // just want any vector perpendicular to forwardVector
                sideVector = Vector3.Cross(forwardVector, Vector3.forward).normalized * thickness;
            else
                sideVector = Vector3.Cross(forwardVector, Vector3.up).normalized * thickness;
            Vector3 upVector = Vector3.Cross(forwardVector, sideVector).normalized * thickness;
            int start = m_Vertices.Count;
            m_Vertices.Add(from + sideVector); //0
            m_Vertices.Add(from - sideVector);
            m_Vertices.Add(from + upVector); //2
            m_Vertices.Add(from - upVector);
            m_Vertices.Add(to + sideVector);//4
            m_Vertices.Add(to - sideVector);
            m_Vertices.Add(to + upVector);// 6
            m_Vertices.Add(to - upVector);

            for (int Idx = 0; Idx < 8; ++Idx)
                m_Colors32.Add(color);

            AddTriangles(start, 0, 2, 4, 4, 2, 6, 1, 5, 2, 2, 5, 6);
            AddTriangles(start, 0, 4, 3, 3, 4, 7, 1, 3, 5, 5, 3, 7);
            AddTriangles(start, 1, 2, 3, 3, 2, 0, 5, 7, 6, 6, 7, 4);
            SetDirty();
        }

        public void DrawPoint(Vector3 point, Color32 color, float radius = 0.1f)
        {
            // draw a point as an octahedron
            int start = m_Vertices.Count;
            m_Vertices.Add(point + new Vector3(radius, 0, 0));
            m_Vertices.Add(point + new Vector3(-radius, 0, 0));
            m_Vertices.Add(point + new Vector3(0, radius, 0));
            m_Vertices.Add(point + new Vector3(0, -radius, 0));
            m_Vertices.Add(point + new Vector3(0, 0, radius));
            m_Vertices.Add(point + new Vector3(0, 0, -radius));

            for (int Idx = 0; Idx < 6; ++Idx)
                m_Colors32.Add(color);

            AddTriangles(start, 0, 2, 4, 0, 5, 2, 0, 4, 3, 0, 3, 5);
            AddTriangles(start, 1, 4, 2, 1, 2, 5, 1, 3, 4, 1, 5, 3);
            SetDirty();
        }

        public void DrawPoint(Vector3 point, string label, Color32 color, float radius = 0.1f)
        {
            DrawPoint(point, color, radius);
            DrawLabel(label, point, color, radius * 1.5f);
        }

        public void DrawArrow(Vector3 from, Vector3 to, Color32 color, float thickness = 0.1f, float arrowheadScale = 3)
        {
            float arrowheadRadius = thickness * arrowheadScale;
            float arrowheadLength = arrowheadRadius * 4;
            Vector3 direction = (to - from).normalized;
            Vector3 arrowheadJoint = to - direction * arrowheadLength;
            DrawLine(from, arrowheadJoint, color, thickness);
            DrawCone(arrowheadJoint, to, color, arrowheadRadius);
        }

        public void DrawCuboid(Vector3 center, Vector3 halfSize, Color32 color)
        {
            DrawParallelepiped(center, new Vector3(halfSize.x, 0, 0), new Vector3(0, halfSize.y, 0), new Vector3(0, 0, halfSize.z), color);
        }

        public void DrawCuboid(Vector3 center, Vector3 halfSize, Quaternion rotation, Color32 color)
        {
            DrawParallelepiped(center,
                rotation * new Vector3(halfSize.x,0,0),
                rotation * new Vector3(0, halfSize.y, 0),
                rotation * new Vector3(0, 0, halfSize.z),
                color);
        }

        public void DrawParallelepiped(Vector3 center, Vector3 x, Vector3 y, Vector3 z, Color32 color)
        {
            int start = m_Vertices.Count;
            m_Vertices.Add(center - x - y - z); // 0
            m_Vertices.Add(center - x - y + z); // 1
            m_Vertices.Add(center - x + y - z); // 2
            m_Vertices.Add(center - x + y + z); // 3
            m_Vertices.Add(center + x - y - z); // 4
            m_Vertices.Add(center + x - y + z); // 5
            m_Vertices.Add(center + x + y - z); // 6
            m_Vertices.Add(center + x + y + z); // 7
            for(int Idx = 0; Idx < 8; ++Idx)
                m_Colors32.Add(color);
            AddQuad(start, 0, 1, 3, 2); // left face
            AddQuad(start, 4, 6, 7, 5); // right face
            AddQuad(start, 2, 3, 7, 6); // top face
            AddQuad(start, 0, 4, 5, 1); // bottom face
            AddQuad(start, 0, 2, 6, 4); // back face
            AddQuad(start, 1, 5, 7, 3); // front face
            SetDirty();
        }

        public void DrawSphere(Vector3 center, Color32 color, float radius, int numDivisions = 8)
        {
            int start = m_Vertices.Count;
            m_Vertices.Add(center + new Vector3(0, radius, 0));
            m_Colors32.Add(color);
            m_Vertices.Add(center - new Vector3(0, radius, 0));
            m_Colors32.Add(color);

            int numRings = (numDivisions / 2) - 1;
            float yawStep = Mathf.PI * 2 / numDivisions;
            float pitchStep = Mathf.PI / (numRings+1);
            int lastRingStart = 2 + numRings - 1;

            for (int vertexIdx = 0; vertexIdx < numDivisions; ++vertexIdx)
            {
                float yaw = vertexIdx * yawStep;
                Vector3 sideVector = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
                for (int ringIdx = 0; ringIdx < numRings; ++ringIdx)
                {
                    float pitch = Mathf.PI * 0.5f - (ringIdx+1) * pitchStep;
                    m_Vertices.Add(center + sideVector * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch));
                    m_Colors32.Add(color);

                    if (ringIdx + 1 < numRings)
                    {
                        AddQuad(start + 2,
                            vertexIdx * numRings + ringIdx,
                            vertexIdx * numRings + ringIdx + 1,
                            ((vertexIdx + 1) % numDivisions) * numRings + ringIdx + 1,
                            ((vertexIdx + 1) % numDivisions) * numRings + ringIdx);
                    }
                }

                AddTriangles(start,
                    0,
                    2 + vertexIdx * numRings,
                    2 + ((vertexIdx + 1) % numDivisions) * numRings,

                    1,
                    lastRingStart + ((vertexIdx + 1) % numDivisions) * numRings,
                    lastRingStart + vertexIdx * numRings
                    );
            }
            SetDirty();
        }

        public void DrawCircle(Vector3 center, Vector3 normal, Color32 color, float radius, int numRingVertices = 8)
        {
            DrawCircleOneSided(center, normal, color, radius, numRingVertices);
            DrawCircleOneSided(center, -normal, color, radius, numRingVertices);
        }

        public void DrawCircleOneSided(Vector3 center, Vector3 normal, Color32 color, float radius, int numRingVertices = 8)
        {
            Vector3 forwardVector = ((Mathf.Abs(normal.z) < Mathf.Abs(normal.x)) ? new Vector3(normal.y, -normal.x, 0) : new Vector3(0, -normal.z, normal.y)).normalized * radius;
            Vector3 sideVector = Vector3.Cross(forwardVector, normal).normalized * radius;

            float angleScale = Mathf.PI * 2.0f / numRingVertices;

            int start = m_Vertices.Count;
            m_Vertices.Add(center);
            m_Colors32.Add(color);

            m_Vertices.Add(center + forwardVector);
            m_Colors32.Add(color);
            for (int step = 1; step < numRingVertices; ++step)
            {
                float angle = step * angleScale;
                m_Vertices.Add(center + forwardVector * Mathf.Cos(angle) + sideVector * Mathf.Sin(angle));
                m_Colors32.Add(color);

                m_Triangles.Add(start); // center
                m_Triangles.Add(start + step + 1); // new ring vertex
                m_Triangles.Add(start + step); // previous ring vertex
            }

            m_Triangles.Add(start); // center
            m_Triangles.Add(start + numRingVertices); // last ring vertex
            m_Triangles.Add(start + 1); // first ring vertex
            SetDirty();
        }

        public void DrawCylinder(Vector3 bottom, Vector3 top, Color32 color, float radius, int numRingVertices = 8)
        {
            int start = m_Vertices.Count;
            Vector3 upVector = top - bottom;
            Vector3 forwardVector = ((Mathf.Abs(upVector.z) < Mathf.Abs(upVector.x)) ? new Vector3(upVector.y, -upVector.x, 0) : new Vector3(0, -upVector.z, upVector.y)).normalized * radius;
            Vector3 sideVector = Vector3.Cross(forwardVector, upVector).normalized * radius;
            float angleScale = Mathf.PI * 2.0f / numRingVertices;

            m_Vertices.Add(bottom);
            m_Colors32.Add(color);

            m_Vertices.Add(top);
            m_Colors32.Add(color);

            m_Vertices.Add(bottom+forwardVector);
            m_Colors32.Add(color);

            m_Vertices.Add(top+forwardVector);
            m_Colors32.Add(color);

            for (int step = 1; step < numRingVertices; ++step)
            {
                float angle = step * angleScale;
                m_Vertices.Add(bottom + forwardVector * Mathf.Cos(angle) + sideVector * Mathf.Sin(angle));
                m_Colors32.Add(color);

                m_Vertices.Add(top + forwardVector * Mathf.Cos(angle) + sideVector * Mathf.Sin(angle));
                m_Colors32.Add(color);

                // bottom circle
                m_Triangles.Add(start); // bottom
                m_Triangles.Add(start + step*2); // previous bottom ring vertex
                m_Triangles.Add(start + step * 2 + 2); // new bottom ring vertex

                // top circle
                m_Triangles.Add(start + 1); // top
                m_Triangles.Add(start + step*2 + 3); // new top ring vertex
                m_Triangles.Add(start + step * 2 + 1); // previous top ring vertex

                // cylinder wall
                AddQuad(start + step*2, 0, 1, 3, 2);
            }

            // bottom circle
            m_Triangles.Add(start); // bottom
            m_Triangles.Add(start + numRingVertices * 2); // last bottom ring vertex
            m_Triangles.Add(start + 2); // first bottom ring vertex

            // top circle
            m_Triangles.Add(start + 1); // top
            m_Triangles.Add(start + 3); // first top ring vertex
            m_Triangles.Add(start + numRingVertices * 2 + 1); // last top ring vertex

            // cylinder wall
            AddQuad(start, 3, 2, numRingVertices * 2, numRingVertices*2+1 );
            SetDirty();
        }

        public void DrawCone(Vector3 basePosition, Vector3 tipPosition, Color32 color, float radius, int numRingVertices = 8)
        {
            int start = m_Vertices.Count;
            m_Vertices.Add(basePosition);
            m_Colors32.Add(new Color32((byte)(color.r/2), (byte)(color.g/2), (byte)(color.b/2),255));
            m_Vertices.Add(tipPosition);
            m_Colors32.Add(color);
            Vector3 heightVector = tipPosition - basePosition;
            Vector3 forwardVector = ((Mathf.Abs(heightVector.z) < Mathf.Abs(heightVector.x)) ? new Vector3(heightVector.y, -heightVector.x, 0) : new Vector3(0, -heightVector.z, heightVector.y)).normalized * radius;
            Vector3 sideVector = Vector3.Cross(forwardVector, heightVector).normalized * radius;
            float angleScale = Mathf.PI * 2.0f / numRingVertices;

            m_Vertices.Add(basePosition + forwardVector);
            m_Colors32.Add(color);

            for (int step = 1; step < numRingVertices; ++step)
            {
                float angle = step*angleScale;
                m_Vertices.Add(basePosition + forwardVector * Mathf.Cos(angle) + sideVector * Mathf.Sin(angle));
                m_Colors32.Add(color);

                m_Triangles.Add(start+1); // tip
                m_Triangles.Add(start+ step+2); // new ring vertex
                m_Triangles.Add(start + step+1); // previous ring vertex

                m_Triangles.Add(start); // base
                m_Triangles.Add(start + step + 1); // previous ring vertex
                m_Triangles.Add(start + step + 2); // new ring vertex
            }

            m_Triangles.Add(start + 1); // tip
            m_Triangles.Add(start + 2); // first ring vertex
            m_Triangles.Add(start + numRingVertices + 1); // last ring vertex

            m_Triangles.Add(start); // base
            m_Triangles.Add(start + numRingVertices + 1); // last ring vertex
            m_Triangles.Add(start + 2); // first ring vertex
            SetDirty();
        }

        public void DrawTriangle(Color32 color, Vector3 a, Vector3 b, Vector3 c)
        {
            int start = m_Vertices.Count;
            m_Vertices.Add(a);
            m_Vertices.Add(b);
            m_Vertices.Add(c);
            for (int Idx = 0; Idx < 3; ++Idx)
                m_Colors32.Add(color);
            AddTriangles(start, 0, 1, 2);
            SetDirty();
        }

        public void DrawTriangleFan(Color32 color, Vector3 center, Vector3 first, params Vector3[] fanPoints)
        {
            int centerIdx = m_Vertices.Count;
            m_Vertices.Add(center);
            m_Colors32.Add(color);
            int currentIdx = m_Vertices.Count;
            m_Vertices.Add(first);
            m_Colors32.Add(color);
            foreach (Vector3 point in fanPoints)
            {
                m_Vertices.Add(point);
                m_Colors32.Add(color);
                m_Triangles.Add(centerIdx);
                m_Triangles.Add(currentIdx);
                m_Triangles.Add(currentIdx + 1);
                currentIdx++;
            }
            SetDirty();
        }

        public void DrawTriangleStrip(Color32 color, Vector3 first, Vector3 second, params Vector3[] otherPoints)
        {
            int currentIdx = m_Vertices.Count;
            m_Vertices.Add(first);
            m_Colors32.Add(color);
            m_Vertices.Add(second);
            m_Colors32.Add(color);
            foreach (Vector3 point in otherPoints)
            {
                m_Vertices.Add(point);
                m_Colors32.Add(color);
                AddTriangles(currentIdx, 0, 1, 2);
                currentIdx++;
            }
            SetDirty();
        }

        public void DrawLines(Color32 color, float thickness, params Vector3[] pointPairs)
        {
            for (int Idx = 1; Idx < pointPairs.Length; Idx += 2)
            {
                DrawLine(pointPairs[Idx - 1], pointPairs[Idx], color, thickness);
            }
        }

        public void DrawLineStrip(Color32 color, float thickness, params Vector3[] stripPoints)
        {
            for (int Idx = 1; Idx < stripPoints.Length; ++Idx)
            {
                DrawLine(stripPoints[Idx - 1], stripPoints[Idx], color, thickness);
            }
        }

        void AddTriangles(int firstIdx, params int[] offsets)
        {
            foreach (int offset in offsets)
            {
                m_Triangles.Add(firstIdx + offset);
            }
        }

        // indices in clockwise order
        void AddQuad(int offset, int a, int b, int c, int d)
        {
            AddQuad(offset + a, offset + b, offset + c, offset + d);
        }

        // indices in clockwise order
        void AddQuad(int a, int b, int c, int d)
        {
            m_Triangles.Add(a);
            m_Triangles.Add(b);
            m_Triangles.Add(c);

            m_Triangles.Add(a);
            m_Triangles.Add(c);
            m_Triangles.Add(d);
        }

        public void DrawLabel(string text, Vector3 position, Color color, float worldSpacing = 0)
        {
            m_Labels.Add(new LabelInfo3D { text = text, position = position, color = color, worldSpacing = worldSpacing });
        }

        public void DrawMesh(Mesh source, Transform transform, Color32 color)
        {
            DrawMesh(source, transform.position, transform.rotation, transform.localScale, color);
        }

        public void DrawMesh(Mesh source, Vector3 position, Quaternion rotation, Vector3 scale, Color32 color)
        {
            GameObject meshObject = new GameObject(source.name);
            m_Supplemental.Add(meshObject);
            meshObject.transform.parent = transform;
            meshObject.transform.position = position;
            meshObject.transform.rotation = rotation;
            meshObject.transform.localScale = scale;
            MeshFilter mfilter = meshObject.AddComponent<MeshFilter>();
            mfilter.sharedMesh = source;
            MeshRenderer mrenderer = meshObject.AddComponent<MeshRenderer>();
            mrenderer.material = (color.a < 255) ? BasicDrawingManager.instance.UnlitColorAlphaMaterial : BasicDrawingManager.instance.UnlitColorMaterial;
            mrenderer.material.color = color;
        }

        public void Clear()
        {
            m_Vertices.Clear();
            m_Colors32.Clear();
            m_Triangles.Clear();
            m_Labels.Clear();
            foreach (GameObject obj in m_Supplemental)
                GameObject.Destroy(obj);
            m_Supplemental.Clear();
            SetDirty();
        }

        public void Destroy()
        {
            GameObject.Destroy(gameObject);
        }

        internal void OnDrawingGUI(Camera cam)
        {
            foreach (LabelInfo3D label in m_Labels)
            {
                Vector3 screenPos = cam.WorldToScreenPoint(label.position + cam.transform.right * label.worldSpacing);
                Vector3 guiPos = GUIUtility.ScreenToGUIPoint(screenPos);
                GUI.color = label.color;
                GUIContent labelContent = new GUIContent(label.text);
                Vector2 labelSize = s_LabelStyle.CalcSize(labelContent);
                labelSize.y *= 2;// no idea why we get bad answers for height

                guiPos.y += labelSize.y * 0.35f;
                GUI.Label(new Rect(guiPos.x, Screen.height - guiPos.y, labelSize.x, labelSize.y), label.text);
            }
        }

        public void Refresh()
        {
            m_isDirty = false;
            m_Mesh.Clear();
            m_Mesh.vertices = m_Vertices.ToArray();
            m_Mesh.colors32 = m_Colors32.ToArray();
            m_Mesh.triangles = m_Triangles.ToArray();
        }

        void SetDirty()
        {
            if (!m_isDirty)
            {
                m_isDirty = true;
                BasicDrawingManager.instance.AddDirty(this);
            }
        }
    }
}