using UnityEngine;

namespace RagdollFight
{
    public enum Part
    {
        Hips, Chest, Head,
        UpperArmL, LowerArmL, UpperArmR, LowerArmR,
        UpperLegL, LowerLegL, UpperLegR, LowerLegR
    }

    /// <summary>
    /// Gang Beasts 스타일의 짧고 통통한 래그돌을 코드로 만든다.
    /// 부위마다 Rigidbody + 캡슐/구 콜라이더, 부위끼리는 ConfigurableJoint(회전만 허용)로 잇는다.
    /// 캐릭터는 +Z를 바라보고, 발바닥이 루트 높이(y = 0)에 오도록 만든다.
    /// </summary>
    public class Ragdoll : MonoBehaviour
    {
        public const int FighterLayer = 8;
        public const int PartCount = 11;

        public Rigidbody[] bodies = new Rigidbody[PartCount];
        public ConfigurableJoint[] joints = new ConfigurableJoint[PartCount];

        public float TotalMass { get; private set; }
        /// <summary>서 있을 때 엉덩이(Hips) 중심 높이. 균형 스프링의 목표값.</summary>
        public float RestHipHeight { get; private set; }

        public Rigidbody this[Part p] => bodies[(int)p];

        readonly Vector2[] baseDrive = new Vector2[PartCount]; // (spring, damper)
        readonly Vector3[] restLocalPos = new Vector3[PartCount];
        Collider[] colliders;

        public static Ragdoll Create(string name, Vector3 position, Quaternion rotation, Color color, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            var ragdoll = go.AddComponent<Ragdoll>();
            ragdoll.Build(color);
            return ragdoll;
        }

        void Build(Color color)
        {
            var bodyMat = new PhysicsMaterial("RagdollBody")
            {
                dynamicFriction = 0.5f, staticFriction = 0.5f, bounciness = 0f
            };
            // 다리는 바닥에 걸리지 않게 마찰을 거의 없앤다. 실제 이동은 엉덩이에 주는 힘이 맡는다.
            var legMat = new PhysicsMaterial("RagdollLeg")
            {
                dynamicFriction = 0.05f, staticFriction = 0.05f, bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum
            };
            Material visual = null;

            // ── 몸통 ──  (위치는 루트 기준 로컬 좌표, 단위 m / kg)
            Sphere(Part.Hips, new Vector3(0f, 0.82f, 0f), 0.24f, 8f, bodyMat, color, ref visual);
            Capsule(Part.Chest, new Vector3(0f, 1.22f, 0f), 0.27f, 0.66f, 10f, bodyMat, color, ref visual);
            Sphere(Part.Head, new Vector3(0f, 1.74f, 0f), 0.21f, 3f, bodyMat, color, ref visual);

            // ── 팔다리 ── 왼쪽 = -X (캐릭터가 +Z를 볼 때)
            for (int side = 0; side < 2; side++)
            {
                float s = side == 0 ? -1f : 1f;
                Part upperArm = side == 0 ? Part.UpperArmL : Part.UpperArmR;
                Part lowerArm = side == 0 ? Part.LowerArmL : Part.LowerArmR;
                Part upperLeg = side == 0 ? Part.UpperLegL : Part.UpperLegR;
                Part lowerLeg = side == 0 ? Part.LowerLegL : Part.LowerLegR;

                Capsule(upperArm, new Vector3(0.37f * s, 1.25f, 0f), 0.09f, 0.36f, 1.5f, bodyMat, color, ref visual);  // 어깨 y 1.43
                Capsule(lowerArm, new Vector3(0.37f * s, 0.89f, 0f), 0.095f, 0.38f, 1.5f, bodyMat, color, ref visual); // 팔꿈치 y 1.07
                Capsule(upperLeg, new Vector3(0.14f * s, 0.55f, 0f), 0.11f, 0.36f, 2.5f, legMat, color, ref visual);   // 고관절 y 0.73
                Capsule(lowerLeg, new Vector3(0.14f * s, 0.19f, 0f), 0.10f, 0.38f, 2.0f, legMat, color, ref visual);   // 무릎 y 0.37
            }

            // ── 관절 ── (spring, damper, X축 회전 한계, Y/Z축 회전 한계)
            Joint(Part.Chest, Part.Hips, new Vector3(0f, 0.92f, 0f), 1000f, 80f, 40f, 30f);
            Joint(Part.Head, Part.Chest, new Vector3(0f, 1.55f, 0f), 300f, 20f, 45f, 40f);
            for (int side = 0; side < 2; side++)
            {
                float s = side == 0 ? -1f : 1f;
                Joint(side == 0 ? Part.UpperArmL : Part.UpperArmR, Part.Chest, new Vector3(0.37f * s, 1.43f, 0f), 300f, 20f, 175f, 90f);
                Joint(side == 0 ? Part.LowerArmL : Part.LowerArmR, side == 0 ? Part.UpperArmL : Part.UpperArmR, new Vector3(0.37f * s, 1.07f, 0f), 150f, 10f, 150f, 20f);
                Joint(side == 0 ? Part.UpperLegL : Part.UpperLegR, Part.Hips, new Vector3(0.14f * s, 0.73f, 0f), 600f, 40f, 100f, 35f);
                Joint(side == 0 ? Part.LowerLegL : Part.LowerLegR, side == 0 ? Part.UpperLegL : Part.UpperLegR, new Vector3(0.14f * s, 0.37f, 0f), 400f, 25f, 140f, 10f);
            }

            RestHipHeight = 0.84f;

            // 자기 몸끼리는 부딪히지 않게 한다 (관절 떨림 방지).
            colliders = GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
                for (int j = i + 1; j < colliders.Length; j++)
                    Physics.IgnoreCollision(colliders[i], colliders[j], true);

            TotalMass = 0f;
            foreach (var rb in bodies) TotalMass += rb.mass;
        }

        Rigidbody MakeBody(Part part, GameObject go, Vector3 localPos, float mass, Collider col, PhysicsMaterial mat, Color color, ref Material visual)
        {
            go.name = part.ToString();
            go.layer = FighterLayer;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            col.sharedMaterial = mat;

            var renderer = go.GetComponent<Renderer>();
            if (visual == null)
            {
                visual = new Material(renderer.sharedMaterial) { name = name + "_Mat" };
                if (visual.HasProperty("_BaseColor")) visual.SetColor("_BaseColor", color);
                if (visual.HasProperty("_Color")) visual.SetColor("_Color", color);
            }
            renderer.sharedMaterial = visual;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.5f;
            rb.maxAngularVelocity = 30f;
            rb.solverIterations = 12;
            rb.solverVelocityIterations = 4;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            bodies[(int)part] = rb;
            restLocalPos[(int)part] = localPos;
            return rb;
        }

        void Sphere(Part part, Vector3 localPos, float radius, float mass, PhysicsMaterial mat, Color color, ref Material visual)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.localScale = Vector3.one * radius * 2f;
            MakeBody(part, go, localPos, mass, go.GetComponent<Collider>(), mat, color, ref visual);
        }

        void Capsule(Part part, Vector3 localPos, float radius, float length, float mass, PhysicsMaterial mat, Color color, ref Material visual)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule); // 기본: 반지름 0.5, 길이 2, Y축 방향
            go.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            MakeBody(part, go, localPos, mass, go.GetComponent<Collider>(), mat, color, ref visual);
        }

        void Joint(Part child, Part parent, Vector3 localAnchor, float spring, float damper, float limitX, float limitYZ)
        {
            var rb = bodies[(int)child];
            var j = rb.gameObject.AddComponent<ConfigurableJoint>();
            // 관절 좌표계 = 부위의 로컬 좌표계 (X = 좌우축, Y = 위). targetRotation 계산을 단순하게 하려는 설정.
            j.axis = Vector3.right;
            j.secondaryAxis = Vector3.up;
            j.anchor = rb.transform.InverseTransformPoint(transform.TransformPoint(localAnchor));
            j.autoConfigureConnectedAnchor = true;

            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;
            j.lowAngularXLimit = new SoftJointLimit { limit = -limitX };
            j.highAngularXLimit = new SoftJointLimit { limit = limitX };
            j.angularYLimit = new SoftJointLimit { limit = limitYZ };
            j.angularZLimit = new SoftJointLimit { limit = limitYZ };

            j.rotationDriveMode = RotationDriveMode.Slerp;
            baseDrive[(int)child] = new Vector2(spring, damper);
            j.slerpDrive = new JointDrive { positionSpring = spring, positionDamper = damper, maximumForce = float.MaxValue };
            j.enableCollision = false;
            j.enablePreprocessing = false;

            j.connectedBody = bodies[(int)parent]; // 마지막에 연결해야 위 설정 기준으로 초기 자세가 잡힌다.
            joints[(int)child] = j;
        }

        /// <summary>
        /// 관절 목표 자세를 정한다. offset은 "서 있는 기본 자세에서 부모 기준으로 얼마나 돌릴지" (부위 로컬 축 기준).
        /// 예: 팔을 앞으로 90도 = Quaternion.Euler(-90, 0, 0).  strength는 관절 힘 배율 (0이면 완전히 흐물).
        /// </summary>
        public void SetDrive(Part part, Quaternion offset, float strength)
        {
            var j = joints[(int)part];
            if (j == null) return;
            j.targetRotation = Quaternion.Inverse(offset);
            Vector2 d = baseDrive[(int)part];
            j.slerpDrive = new JointDrive
            {
                positionSpring = d.x * strength,
                positionDamper = d.y * Mathf.Max(strength, 0.15f),
                maximumForce = float.MaxValue
            };
        }

        /// <summary>모든 부위를 기본 자세로 순간이동시키고 속도를 0으로 만든다 (라운드 리셋·학습 에피소드 리셋용).</summary>
        public void ResetPose(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            for (int i = 0; i < PartCount; i++)
            {
                var rb = bodies[i];
                Vector3 wp = transform.TransformPoint(restLocalPos[i]);
                rb.transform.SetPositionAndRotation(wp, rotation);
                rb.position = wp;
                rb.rotation = rotation;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        public bool Owns(Rigidbody rb) => rb != null && rb.transform.IsChildOf(transform);
    }
}
