using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif



namespace UTJ
{
    [ExecuteInEditMode]
    [AddComponentMenu("UTJ/USD/Exporter")]
    public class usdiExporter : MonoBehaviour
    {
        #region impl
    
        public static IntPtr GetArrayPtr(Array v)
        {
            return Marshal.UnsafeAddrOfPinnedArrayElement(v, 0);
        }
    
        public static void CaptureTransform(
            usdi.Xform usd, Transform trans, double t,
            bool inherits, bool invertForward, bool scale)
        {
            usdi.XformData data;

            if (invertForward) { trans.forward = trans.forward * -1.0f; }
            if (inherits)
            {
                data.position = trans.localPosition;
                data.rotation = trans.localRotation;
                data.scale = scale ? trans.localScale : Vector3.one;
            }
            else
            {
                data.position = trans.position;
                data.rotation = trans.rotation;
                data.scale = scale ? trans.lossyScale : Vector3.one;
            }

            if (invertForward) { trans.forward = trans.forward * -1.0f; }
            usdi.usdiXformWriteSample(usd, ref data, t);
        }
    
        public static void CaptureCamera(usdi.Camera usd, Camera cam, double t/*, AlembicCameraParams cparams = null*/)
        {
            var data = usdi.CameraData.default_value;
            data.near_clipping_plane = cam.nearClipPlane;
            data.far_clipping_plane = cam.farClipPlane;
            data.field_of_view = cam.fieldOfView;
            //if(cparams != null)
            //{
            //    data.focal_length = cparams.m_focalLength;
            //    data.focus_distance = cparams.m_focusDistance;
            //    data.aperture = cparams.m_aperture;
            //    data.aspect_ratio = cparams.GetAspectRatio();
            //}
            usdi.usdiCameraWriteSample(usd, ref data, t);
        }
    
        public class MeshBuffer
        {
            public int[] indices;
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector2[] uvs;
        }
    
        public static void CaptureMesh(usdi.Mesh usd, Mesh mesh, Cloth cloth, MeshBuffer dst_buf, double t)
        {
            dst_buf.indices = mesh.triangles;
            dst_buf.uvs = mesh.uv;
            if (cloth == null)
            {
                dst_buf.vertices = mesh.vertices;
                dst_buf.normals = mesh.normals;
            }
            else
            {
                dst_buf.vertices = cloth.vertices;
                dst_buf.normals = cloth.normals;
            }

            usdi.MeshData data = default(usdi.MeshData);
            data.indices = GetArrayPtr(dst_buf.indices);
            data.points = GetArrayPtr(dst_buf.vertices);
            if(dst_buf.normals != null) { data.normals = GetArrayPtr(dst_buf.normals); }
            //if(dst_buf.uvs != null) { data.uvs = GetArrayPtr(dst_buf.uvs); }
            data.num_points = dst_buf.vertices.Length;
            data.num_indices = dst_buf.indices.Length;
    
            usdi.usdiMeshWriteSample(usd, ref data, t);
        }
    
    
        static string CreateName(UnityEngine.Object target)
        {
            return target.name + "_" + target.GetInstanceID().ToString("X8");
        }
    
        public abstract class ComponentCapturer
        {
            protected ComponentCapturer m_parent;
            protected GameObject m_obj;
            protected usdi.Schema m_usd;

            public ComponentCapturer parent { get { return m_parent; } }
            public GameObject obj { get { return m_obj; } }
            public usdi.Schema usd { get { return m_usd; } }
            public abstract void Capture(double t);

            protected ComponentCapturer(ComponentCapturer p)
            {
                m_parent = p;
            }
        }

        public class RootCapturer : ComponentCapturer
        {
            public RootCapturer(usdi.Schema usd)
                : base(null)
            {
                m_usd = usd;
            }

            public override void Capture(double t)
            {
                // do nothing
            }
        }

        public class TransformCapturer : ComponentCapturer
        {
            Transform m_target;
            bool m_inherits = true;
            bool m_invertForward = false;
            bool m_scale = true;

            public bool inherits {
                get { return m_inherits; }
                set { m_inherits = value; }
            }
            public bool invertForward
            {
                get { return m_invertForward; }
                set { m_invertForward = value; }
            }
            public bool scale
            {
                get { return m_scale; }
                set { m_scale = value; }
            }

            public TransformCapturer(usdi.Context ctx, ComponentCapturer parent, Transform target, bool create_usd_node = true)
                : base(parent)
            {
                m_obj = target.gameObject;
                m_target = target;
                if(create_usd_node)
                {
                    m_usd = usdi.usdiCreateXform(ctx, parent.usd, CreateName(target));
                }
            }

            public override void Capture(double t)
            {
                if (m_target == null) { return; }
    
                CaptureTransform(usdi.usdiAsXform(m_usd), m_target, t, m_inherits, m_invertForward, m_scale);
            }
        }
    
        public class CameraCapturer : TransformCapturer
        {
            Camera m_target;
            //AlembicCameraParams m_params;
    
            public CameraCapturer(usdi.Context ctx, ComponentCapturer parent, Camera target)
                : base(ctx, parent, target.GetComponent<Transform>(), false)
            {
                m_obj = target.gameObject;
                m_usd = usdi.usdiCreateCamera(ctx, parent.usd, CreateName(target));
                m_target = target;
                //m_params = target.GetComponent<AlembicCameraParams>();
            }
    
            public override void Capture(double t)
            {
                if (m_target == null) { return; }

                base.Capture(t);
                CaptureCamera(usdi.usdiAsCamera(m_usd), m_target, t/*, m_params*/);
            }
        }
    
        public class MeshCapturer : TransformCapturer
        {
            MeshRenderer m_target;
            MeshBuffer m_mesh_buffer;
    
            public MeshCapturer(usdi.Context ctx, ComponentCapturer parent, MeshRenderer target)
                : base(ctx, parent, target.GetComponent<Transform>(), false)
            {
                m_obj = target.gameObject;
                m_usd = usdi.usdiCreateMesh(ctx, parent.usd, CreateName(target));
                m_target = target;
                m_mesh_buffer = new MeshBuffer();
            }
    
            public override void Capture(double t)
            {
                if (m_target == null) { return; }

                base.Capture(t);
                CaptureMesh(usdi.usdiAsMesh(m_usd), m_target.GetComponent<MeshFilter>().sharedMesh, null, m_mesh_buffer, t);
            }
        }
    
        public class SkinnedMeshCapturer : TransformCapturer
        {
            SkinnedMeshRenderer m_target;
            Mesh m_mesh;
            MeshBuffer m_mesh_buffer;
    
            public SkinnedMeshCapturer(usdi.Context ctx, ComponentCapturer parent, SkinnedMeshRenderer target)
                : base(ctx, parent, target.GetComponent<Transform>(), false)
            {
                m_obj = target.gameObject;
                m_usd = usdi.usdiCreateMesh(ctx, parent.usd, CreateName(target));
                m_target = target;
                m_mesh_buffer = new MeshBuffer();

                if (m_target.GetComponent<Cloth>() != null)
                {
                    var t = m_parent as TransformCapturer;
                    if (t != null)
                    {
                        t.scale = false;
                    }
                }
            }

            public override void Capture(double t)
            {
                if (m_target == null) { return; }

                base.Capture(t);

                if (m_mesh == null) { m_mesh = new Mesh(); }
                m_target.BakeMesh(m_mesh);
                CaptureMesh(usdi.usdiAsMesh(m_usd), m_mesh, m_target.GetComponent<Cloth>(), m_mesh_buffer, t);
            }
        }
    
        public class ParticleCapturer : TransformCapturer
        {
            ParticleSystem m_target;
            //AbcAPI.aeProperty m_prop_rotatrions;
    
            ParticleSystem.Particle[] m_buf_particles;
            Vector3[] m_buf_positions;
            Vector4[] m_buf_rotations;
    
            public ParticleCapturer(usdi.Context ctx, ComponentCapturer parent, ParticleSystem target)
                : base(ctx, parent, target.GetComponent<Transform>(), false)
            {
                m_obj = target.gameObject;
                m_usd = usdi.usdiCreatePoints(ctx, parent.usd, CreateName(target));
                m_target = target;
    
                //m_prop_rotatrions = AbcAPI.aeNewProperty(m_usd, "rotation", AbcAPI.aePropertyType.Float4Array);
            }
    
            public override void Capture(double t)
            {
                if (m_target == null) { return; }

                base.Capture(t);

                // create buffer
                int count_max = m_target.maxParticles;
                if (m_buf_particles == null)
                {
                    m_buf_particles = new ParticleSystem.Particle[count_max];
                    m_buf_positions = new Vector3[count_max];
                    m_buf_rotations = new Vector4[count_max];
                }
                else if (m_buf_particles.Length != count_max)
                {
                    Array.Resize(ref m_buf_particles, count_max);
                    Array.Resize(ref m_buf_positions, count_max);
                    Array.Resize(ref m_buf_rotations, count_max);
                }
    
                // copy particle positions & rotations to buffer
                int count = m_target.GetParticles(m_buf_particles);
                for (int i = 0; i < count; ++i)
                {
                    m_buf_positions[i] = m_buf_particles[i].position;
                }
                for (int i = 0; i < count; ++i)
                {
                    m_buf_rotations[i] = m_buf_particles[i].axisOfRotation;
                    m_buf_rotations[i].w = m_buf_particles[i].rotation;
                }
    
                // write!
                var data = default(usdi.PointsData);
                data.points = GetArrayPtr(m_buf_positions);
                data.num_points = count;
                usdi.usdiPointsWriteSample(usdi.usdiAsPoints(m_usd), ref data, t);
                //AbcAPI.aePropertyWriteArraySample(m_prop_rotatrions, GetArrayPtr(m_buf_rotations), count);
            }
        }
    
        public class CustomCapturerHandler : TransformCapturer
        {
            usdiCustomComponentCapturer m_target;

            public CustomCapturerHandler(usdi.Context ctx, ComponentCapturer parent, usdiCustomComponentCapturer target)
                : base(ctx, parent, target.GetComponent<Transform>(), false)
            {
                m_obj = target.gameObject;
                m_target = target;
            }
    
            public override void Capture(double t)
            {
                if (m_target == null) { return; }

                base.Capture(t);
                m_target.Capture(t);
            }
        }
    
    
    #if UNITY_EDITOR
        void ForceDisableBatching()
        {
            var method = typeof(UnityEditor.PlayerSettings).GetMethod("SetBatchingForPlatform", BindingFlags.NonPublic | BindingFlags.Static);
            method.Invoke(null, new object[] { BuildTarget.StandaloneWindows, 0, 0 });
            method.Invoke(null, new object[] { BuildTarget.StandaloneWindows64, 0, 0 });
        }
    #endif
    
        #endregion
    
    
        public enum Scope
        {
            EntireScene,
            CurrentBranch,
        }
    
        [Header("USD")]
    
        public string m_outputPath;
        public float m_scale = 1.0f;
        public bool m_swapHandedness = true;
        public bool m_swapFaces = true;
    
        [Header("Capture Components")]
    
        public Scope m_scope = Scope.EntireScene;
        public bool m_preserveTreeStructure = true;
        public bool m_ignoreDisabled = true;
        [Space(8)]
        public bool m_captureMeshRenderer = true;
        public bool m_captureSkinnedMeshRenderer = true;
        public bool m_captureParticleSystem = true;
        public bool m_captureCamera = true;
        public bool m_customCapturer = true;
    
        [Header("Capture Setting")]
    
        [Tooltip("Start capture on start.")]
        public bool m_captureOnStart = false;
        [Tooltip("Automatically end capture when reached Max Capture Frame. 0=Infinite")]
        public int m_maxCaptureFrame = 0;
    
        [Header("Misc")]
    
        public bool m_detailedLog;
    
        usdi.Context m_ctx;
        ComponentCapturer m_root;
        List<ComponentCapturer> m_capturers = new List<ComponentCapturer>();
        bool m_recording;
        float m_time;
        float m_elapsed;
        int m_frameCount;
    
    
        public bool isRecording { get { return m_recording; } }
        public float time { get { return m_time; } }
        public float elapsed { get { return m_elapsed; } }
        public float frameCount { get { return m_frameCount; } }
    
    
        T[] GetTargets<T>() where T : Component
        {
            if(m_scope == Scope.CurrentBranch)
            {
                return GetComponentsInChildren<T>();
            }
            else
            {
                return FindObjectsOfType<T>();
            }
        }
    
    
        public TransformCapturer CreateComponentCapturer(usdi.Context ctx, ComponentCapturer parent, Transform target)
        {
            if (m_detailedLog) { Debug.Log("usdiExporter: new TransformCapturer(\"" + target.name + "\""); }
    
            var cap = new TransformCapturer(ctx, parent, target);
            m_capturers.Add(cap);
            return cap;
        }

        public CameraCapturer CreateComponentCapturer(usdi.Context ctx, ComponentCapturer parent, Camera target)
        {
            if (m_detailedLog) { Debug.Log("usdiExporter: new CameraCapturer(\"" + target.name + "\""); }
    
            var cap = new CameraCapturer(ctx, parent, target);
            m_capturers.Add(cap);
            return cap;
        }
    
        public MeshCapturer CreateComponentCapturer(usdi.Context ctx, ComponentCapturer parent, MeshRenderer target)
        {
            if (m_detailedLog) { Debug.Log("usdiExporter: new MeshCapturer(\"" + target.name + "\""); }
    
            var cap = new MeshCapturer(ctx, parent, target);
            m_capturers.Add(cap);
            return cap;
        }
    
        public SkinnedMeshCapturer CreateComponentCapturer(usdi.Context ctx, ComponentCapturer parent, SkinnedMeshRenderer target)
        {
            if (m_detailedLog) { Debug.Log("usdiExporter: new SkinnedMeshCapturer(\"" + target.name + "\""); }
    
            var cap = new SkinnedMeshCapturer(ctx, parent, target);
            m_capturers.Add(cap);
            return cap;
        }
    
        public ParticleCapturer CreateComponentCapturer(usdi.Context ctx, ComponentCapturer parent, ParticleSystem target)
        {
            if (m_detailedLog) { Debug.Log("usdiExporter: new ParticleCapturer(\"" + target.name + "\""); }
    
            var cap = new ParticleCapturer(ctx, parent, target);
            m_capturers.Add(cap);
            return cap;
        }
    
        public CustomCapturerHandler CreateComponentCapturer(usdi.Context ctx, ComponentCapturer parent, usdiCustomComponentCapturer target)
        {
            if (m_detailedLog) { Debug.Log("usdiExporter: new CustomCapturerHandler(\"" + target.name + "\""); }
    
            target.CreateAbcObject(ctx, parent.usd);
            var cap = new CustomCapturerHandler(ctx, parent, target);
            m_capturers.Add(cap);
            return cap;
        }
    
        bool ShouldBeIgnored(Behaviour target)
        {
            return m_ignoreDisabled && (!target.gameObject.activeInHierarchy || !target.enabled);
        }
        bool ShouldBeIgnored(ParticleSystem target)
        {
            return m_ignoreDisabled && (!target.gameObject.activeInHierarchy);
        }
        bool ShouldBeIgnored(MeshRenderer target)
        {
            if (m_ignoreDisabled && (!target.gameObject.activeInHierarchy || !target.enabled)) { return true; }
            var mesh = target.GetComponent<MeshFilter>().sharedMesh;
            if (mesh == null) { return true; }
            return false;
        }
        bool ShouldBeIgnored(SkinnedMeshRenderer target)
        {
            if (m_ignoreDisabled && (!target.gameObject.activeInHierarchy || !target.enabled)) { return true; }
            var mesh = target.sharedMesh;
            if (mesh == null) { return true; }
            return false;
        }
    
    
        #region impl_capture_tree
    
        // capture node tree for "Preserve Tree Structure" option.
        public class CaptureNode
        {
            public CaptureNode parent;
            public List<CaptureNode> children = new List<CaptureNode>();
            public Type componentType;
    
            public Transform trans;
            public ComponentCapturer capturer;
        }
    
        Dictionary<Transform, CaptureNode> m_capture_node;
        List<CaptureNode> m_top_nodes;
    
        CaptureNode ConstructTree(Transform trans)
        {
            if(trans == null) { return null; }
            if (m_detailedLog) Debug.Log("ConstructTree() : " + trans.name);
    
            CaptureNode cn;
            if (m_capture_node.TryGetValue(trans, out cn)) { return cn; }
    
            cn = new CaptureNode();
            cn.trans = trans;
            m_capture_node.Add(trans, cn);
    
            var parent = ConstructTree(trans.parent);
            if (parent != null)
            {
                parent.children.Add(cn);
            }
            else
            {
                m_top_nodes.Add(cn);
            }
    
            return cn;
        }
    
        void SetupComponentCapturer(CaptureNode parent, CaptureNode node)
        {
            if(m_detailedLog) Debug.Log("SetupComponentCapturer() " + node.trans.name);
            node.parent = parent;
            var parent_capturer = parent == null ? m_root : parent.capturer;


            if (node.componentType == null)
            {
                node.capturer = CreateComponentCapturer(m_ctx, parent_capturer, node.trans);
            }
            else if (node.componentType == typeof(Camera))
            {
                node.capturer = CreateComponentCapturer(m_ctx, parent_capturer, node.trans.GetComponent<Camera>());
                (node.capturer as TransformCapturer).invertForward = true;
            }
            else if (node.componentType == typeof(MeshRenderer))
            {
                node.capturer = CreateComponentCapturer(m_ctx, parent_capturer, node.trans.GetComponent<MeshRenderer>());
            }
            else if (node.componentType == typeof(SkinnedMeshRenderer))
            {
                node.capturer = CreateComponentCapturer(m_ctx, parent_capturer, node.trans.GetComponent<SkinnedMeshRenderer>());
            }
            else if (node.componentType == typeof(ParticleSystem))
            {
                node.capturer = CreateComponentCapturer(m_ctx, parent_capturer, node.trans.GetComponent<ParticleSystem>());
            }
            else if (node.componentType == typeof(usdiCustomComponentCapturer))
            {
                node.capturer = CreateComponentCapturer(m_ctx, parent_capturer, node.trans.GetComponent<usdiCustomComponentCapturer>());
            }
    
            foreach (var c in node.children)
            {
                SetupComponentCapturer(node, c);
            }
        }
        #endregion
    
        void CreateCapturers_Tree()
        {
            m_root = new RootCapturer(usdi.usdiGetRoot(m_ctx));
            m_capture_node = new Dictionary<Transform, CaptureNode>();
            m_top_nodes = new List<CaptureNode>();
    
            // construct tree
            // (bottom-up)
            if (m_captureCamera)
            {
                foreach (var t in GetTargets<Camera>())
                {
                    if (ShouldBeIgnored(t)) { continue; }
                    var node = ConstructTree(t.GetComponent<Transform>());
                    node.componentType = t.GetType();
                }
            }
            if (m_captureMeshRenderer)
            {
                foreach (var t in GetTargets<MeshRenderer>())
                {
                    if (ShouldBeIgnored(t)) { continue; }
                    var node = ConstructTree(t.GetComponent<Transform>());
                    node.componentType = t.GetType();
                }
            }
            if (m_captureSkinnedMeshRenderer)
            {
                foreach (var t in GetTargets<SkinnedMeshRenderer>())
                {
                    if (ShouldBeIgnored(t)) { continue; }
                    var node = ConstructTree(t.GetComponent<Transform>());
                    node.componentType = t.GetType();
                }
            }
            if (m_captureParticleSystem)
            {
                foreach (var t in GetTargets<ParticleSystem>())
                {
                    if (ShouldBeIgnored(t)) { continue; }
                    var node = ConstructTree(t.GetComponent<Transform>());
                    node.componentType = t.GetType();
                }
            }
            if (m_customCapturer)
            {
                foreach (var t in GetTargets<usdiCustomComponentCapturer>())
                {
                    if (ShouldBeIgnored(t)) { continue; }
                    var node = ConstructTree(t.GetComponent<Transform>());
                    node.componentType = typeof(usdiCustomComponentCapturer);
                }
            }
    
            // make component capturers (top-down)
            foreach (var c in m_top_nodes)
            {
                SetupComponentCapturer(null, c);
            }
    
    
            m_top_nodes = null;
            m_capture_node = null;
        }
    
        void CreateCapturers_Flat()
        {
            m_root = new RootCapturer(usdi.usdiGetRoot(m_ctx));

            // Camera
            if (m_captureCamera)
            {
                foreach (var target in GetTargets<Camera>())
                {
                    if (ShouldBeIgnored(target)) { continue; }
                    var cc = CreateComponentCapturer(m_ctx, m_root, target);
                    (cc as TransformCapturer).invertForward = true;
                }
            }
    
            // MeshRenderer
            if (m_captureMeshRenderer)
            {
                foreach (var target in GetTargets<MeshRenderer>())
                {
                    if (ShouldBeIgnored(target)) { continue; }
                    CreateComponentCapturer(m_ctx, m_root, target);
                }
            }
    
            // SkinnedMeshRenderer
            if (m_captureSkinnedMeshRenderer)
            {
                foreach (var target in GetTargets<SkinnedMeshRenderer>())
                {
                    if (ShouldBeIgnored(target)) { continue; }
                    CreateComponentCapturer(m_ctx, m_root, target);
                }
            }
    
            // ParticleSystem
            if (m_captureParticleSystem)
            {
                foreach (var target in GetTargets<ParticleSystem>())
                {
                    if (ShouldBeIgnored(target)) { continue; }
                    CreateComponentCapturer(m_ctx, m_root, target);
                }
            }
    
            // handle custom capturers (usdiCustomComponentCapturer)
            if (m_customCapturer)
            {
                foreach (var target in GetTargets<usdiCustomComponentCapturer>())
                {
                    if (ShouldBeIgnored(target)) { continue; }
                    CreateComponentCapturer(m_ctx, m_root, target);
                }
            }
        }

        void ApplyExportConfig()
        {
            usdi.ExportConfig conf = usdi.ExportConfig.default_value;
            conf.scale = m_scale;
            conf.swap_handedness = m_swapHandedness;
            conf.swap_faces = m_swapFaces;
            usdi.usdiSetExportConfig(m_ctx, ref conf);
        }


        public bool BeginCapture()
        {
            if(m_recording) {
                Debug.Log("usdiExporter: already started");
                return false;
            }
    
            // create context and open archive
            m_ctx = usdi.usdiCreateContext();
            usdi.usdiCreateStage(m_ctx, m_outputPath);
            ApplyExportConfig();

            // create capturers
            if (m_preserveTreeStructure) {
                CreateCapturers_Tree();
            }
            else {
                CreateCapturers_Flat();
            }
    
            m_recording = true;
            //m_time = m_conf.startTime;
            m_frameCount = 0;
        
            Debug.Log("usdiExporter: start " + m_outputPath);
            return true;
        }
    
        public void EndCapture()
        {
            if (!m_recording) { return; }
    
            m_capturers.Clear();
            usdi.usdiWrite(m_ctx, m_outputPath);
            usdi.usdiDestroyContext(m_ctx); // flush archive
            m_ctx = default(usdi.Context);
            m_recording = false;
            m_time = 0.0f;
            m_frameCount = 0;
    
            Debug.Log("usdiExporter: end: " + m_outputPath);
        }
    
        public void OneShot()
        {
            if (BeginCapture())
            {
                ProcessCapture();
                EndCapture();
            }
        }
    
        void ProcessCapture()
        {
            if (!m_recording) { return; }
    
            float begin_time = Time.realtimeSinceStartup;
    
            foreach (var recorder in m_capturers)
            {
                recorder.Capture(m_time);
            }
            m_time += Time.deltaTime;
            ++m_frameCount;
    
            m_elapsed = Time.realtimeSinceStartup - begin_time;
            if (m_detailedLog)
            {
                Debug.Log("usdiExporter.ProcessCapture(): " + (m_elapsed * 1000.0f) + "ms");
            }
    
            if(m_maxCaptureFrame > 0 && m_frameCount >= m_maxCaptureFrame)
            {
                EndCapture();
            }
        }
    
        IEnumerator ProcessRecording()
        {
            yield return new WaitForEndOfFrame();
            if(!m_recording) { yield break; }
    
            ProcessCapture();
    
            //// wait maximumDeltaTime if timeSamplingType is uniform
            //if (m_conf.timeSamplingType == AbcAPI.aeTypeSamplingType.Uniform)
            //{
            //    AbcAPI.aeWaitMaxDeltaTime();
            //}
        }
    
        void UpdateOutputPath()
        {
            if (m_outputPath == null || m_outputPath == "")
            {
                m_outputPath = "Assets/StreamingAssets/" + gameObject.name + ".usda";
            }
        }
    
    
    
    #if UNITY_EDITOR
        void Reset()
        {
            ForceDisableBatching();
            UpdateOutputPath();
        }
    #endif
    
        void OnEnable()
        {
            UpdateOutputPath();
        }
    
        void Start()
        {
    #if UNITY_EDITOR
            if(m_captureOnStart && EditorApplication.isPlaying)
    #else
            if(m_captureOnStart)
    #endif
            {
                BeginCapture();
            }
        }
    
        void Update()
        {
            if(m_recording)
            {
                StartCoroutine(ProcessRecording());
            }
        }
    
        void OnDisable()
        {
            EndCapture();
        }
    }
}
