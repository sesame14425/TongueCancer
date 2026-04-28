using System.Collections.Generic;
using UnityEngine;

namespace TongueCancer.TissueDeformation
{
    /// <summary>
    /// 質量彈簧系統（Mass-Spring System）用於模擬虛擬舌頭組織的彈性變形。
    /// 每個網格頂點被視為質量節點，節點之間以彈簧連接。
    /// Mass-Spring System for simulating elastic deformation of virtual tongue tissue.
    /// Each mesh vertex is treated as a mass node connected by springs.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    public class MassSpringDeformation : MonoBehaviour
    {
        [Header("物理參數 / Physics Parameters")]
        [Tooltip("彈簧剛度係數 Spring stiffness coefficient")]
        [Range(0.1f, 500f)]
        public float springStiffness = 50f;

        [Tooltip("阻尼係數 Damping coefficient")]
        [Range(0f, 1f)]
        public float damping = 0.1f;

        [Tooltip("節點質量 Node mass (kg)")]
        [Range(0.001f, 1f)]
        public float nodeMass = 0.01f;

        [Tooltip("最大變形距離 Maximum deformation distance (m)")]
        [Range(0f, 0.5f)]
        public float maxDeformationDistance = 0.05f;

        [Header("組織恢復 / Tissue Recovery")]
        [Tooltip("啟用組織彈性恢復 Enable elastic tissue recovery")]
        public bool enableElasticRecovery = true;

        [Tooltip("恢復速率 Recovery rate")]
        [Range(0f, 10f)]
        public float recoveryRate = 2f;

        // Internal state
        private Mesh _mesh;
        private Vector3[] _originalVertices;
        private Vector3[] _currentVertices;
        private Vector3[] _velocities;
        private List<Spring> _springs = new List<Spring>();

        private struct Spring
        {
            public int NodeA;
            public int NodeB;
            public float RestLength;
        }

        private void Awake()
        {
            InitializeMesh();
        }

        private void InitializeMesh()
        {
            _mesh = GetComponent<MeshFilter>().mesh;
            _originalVertices = _mesh.vertices;
            _currentVertices = (Vector3[])_originalVertices.Clone();
            _velocities = new Vector3[_originalVertices.Length];

            BuildSprings();
        }

        /// <summary>
        /// 根據網格邊建立彈簧結構。
        /// Builds spring structure from mesh edges.
        /// </summary>
        private void BuildSprings()
        {
            _springs.Clear();
            int[] triangles = _mesh.triangles;

            HashSet<long> addedEdges = new HashSet<long>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                AddSpringEdge(triangles[i], triangles[i + 1], addedEdges);
                AddSpringEdge(triangles[i + 1], triangles[i + 2], addedEdges);
                AddSpringEdge(triangles[i], triangles[i + 2], addedEdges);
            }
        }

        private void AddSpringEdge(int a, int b, HashSet<long> addedEdges)
        {
            int lo = Mathf.Min(a, b);
            int hi = Mathf.Max(a, b);
            long key = ((long)lo << 32) | (uint)hi;
            if (addedEdges.Add(key))
            {
                _springs.Add(new Spring
                {
                    NodeA = a,
                    NodeB = b,
                    RestLength = Vector3.Distance(_originalVertices[a], _originalVertices[b])
                });
            }
        }

        private void FixedUpdate()
        {
            SimulateStep(Time.fixedDeltaTime);
        }

        /// <summary>
        /// 執行一個模擬時間步驟。
        /// Executes one simulation time step.
        /// </summary>
        private void SimulateStep(float dt)
        {
            Vector3[] forces = new Vector3[_currentVertices.Length];

            // 計算彈簧力 / Compute spring forces
            foreach (Spring spring in _springs)
            {
                Vector3 delta = _currentVertices[spring.NodeB] - _currentVertices[spring.NodeA];
                float currentLength = delta.magnitude;
                if (currentLength < 1e-6f) continue;

                float stretch = currentLength - spring.RestLength;
                Vector3 forceDir = delta / currentLength;
                Vector3 springForce = springStiffness * stretch * forceDir;

                forces[spring.NodeA] += springForce;
                forces[spring.NodeB] -= springForce;
            }

            // 積分速度與位置 / Integrate velocity and position
            for (int i = 0; i < _currentVertices.Length; i++)
            {
                Vector3 acceleration = forces[i] / nodeMass;
                _velocities[i] += acceleration * dt;
                _velocities[i] *= (1f - damping * dt); // 阻尼衰減 damping

                // 彈性恢復力 / Elastic recovery force
                if (enableElasticRecovery)
                {
                    Vector3 displacement = _currentVertices[i] - _originalVertices[i];
                    _velocities[i] -= displacement * recoveryRate * dt;
                }

                _currentVertices[i] += _velocities[i] * dt;

                // 限制最大變形距離 / Clamp maximum deformation
                Vector3 totalDisplacement = _currentVertices[i] - _originalVertices[i];
                if (totalDisplacement.magnitude > maxDeformationDistance)
                {
                    _currentVertices[i] = _originalVertices[i] + totalDisplacement.normalized * maxDeformationDistance;
                    _velocities[i] = Vector3.zero;
                }
            }

            _mesh.vertices = _currentVertices;
            _mesh.RecalculateNormals();
        }

        /// <summary>
        /// 在指定世界座標位置施加力於最近的節點。
        /// Applies force to the nearest node at the specified world position.
        /// </summary>
        /// <param name="worldPosition">施力位置（世界座標）Force application position (world space)</param>
        /// <param name="force">施加的力（世界座標）Force to apply (world space)</param>
        /// <param name="radius">影響半徑 Influence radius</param>
        public void ApplyForceAtPosition(Vector3 worldPosition, Vector3 force, float radius = 0.01f)
        {
            Vector3 localPos = transform.InverseTransformPoint(worldPosition);
            Vector3 localForce = transform.InverseTransformDirection(force);

            for (int i = 0; i < _currentVertices.Length; i++)
            {
                float dist = Vector3.Distance(_currentVertices[i], localPos);
                if (dist < radius)
                {
                    float falloff = 1f - (dist / radius);
                    _velocities[i] += localForce * falloff / nodeMass;
                }
            }
        }

        /// <summary>
        /// 重置所有頂點回初始位置。
        /// Resets all vertices to their original positions.
        /// </summary>
        public void ResetDeformation()
        {
            _currentVertices = (Vector3[])_originalVertices.Clone();
            _velocities = new Vector3[_originalVertices.Length];
            _mesh.vertices = _currentVertices;
            _mesh.RecalculateNormals();
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                _mesh.vertices = _originalVertices;
        }
    }
}
