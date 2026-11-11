using UnityEngine;

namespace RagdollFight
{
    /// <summary>
    /// 아래팔(손)에 붙는다. 잡기 버튼을 누른 상태에서 손끝이 무언가에 닿으면 FixedJoint로 붙고,
    /// 버튼을 떼면 놓는다. 너무 세게 당기면 관절이 끊어져 손이 빠진다.
    /// 상대 몸뿐 아니라 바닥·경기장 가장자리 같은 고정 물체도 잡을 수 있다 (Human: Fall Flat 방식).
    /// 오래 버티는 건 악력 게이지가 막는다.
    /// </summary>
    public class HandGrabber : MonoBehaviour
    {
        public FighterController owner;
        public bool wantGrab;
        public float breakForce = 6000f;
        public float grabRadius = 0.16f;
        [Tooltip("바닥·벽 같은 고정 물체도 잡을 수 있는지")]
        public bool allowWorldGrab = true;

        FixedJoint joint;
        Collider worldCollider;
        Vector3 worldAnchor;
        static readonly Collider[] overlapBuffer = new Collider[16];

        public bool IsGrabbing => joint != null;
        /// <summary>바닥·벽 등 고정 물체를 잡고 있는지</summary>
        public bool IsGrabbingWorld => joint != null && worldCollider != null;
        public Rigidbody Grabbed => joint != null ? joint.connectedBody : null;
        /// <summary>상대 파이터를 잡고 있는지 (보상·관측용)</summary>
        public FighterController GrabbedFighter { get; private set; }

        /// <summary>손끝 위치 (아래팔 캡슐의 끝).</summary>
        public Vector3 TipPosition => transform.TransformPoint(0f, -1f, 0f);
        /// <summary>고정 물체를 잡은 지점</summary>
        public Vector3 WorldAnchor => worldAnchor;

        /// <summary>
        /// 잡기 버튼을 누르고 있는 동안 매 스텝 호출된다. 손끝 주변에서 가장 가까운 물체에 붙는다.
        /// (충돌 콜백 대신 겹침 검사를 써서 학습 중에도 결과가 일정하다.)
        /// </summary>
        public void TryGrabNearby()
        {
            if (!wantGrab || joint != null) return;
            Vector3 tip = TipPosition;
            int n = Physics.OverlapSphereNonAlloc(tip, grabRadius, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            Collider best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var col = overlapBuffer[i];
                var rb = col.attachedRigidbody;
                if (rb != null)
                {
                    if (rb.isKinematic || owner.Owns(rb)) continue;
                }
                else if (!allowWorldGrab || !CanQueryClosestPoint(col)) continue;

                float d = (col.ClosestPoint(tip) - tip).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = col; }
            }
            if (best == null) return;

            var target = best.attachedRigidbody;
            joint = gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = target; // null이면 월드(그 자리)에 고정된다
            joint.breakForce = breakForce;
            joint.breakTorque = breakForce;
            joint.enableCollision = false;
            if (target != null)
            {
                GrabbedFighter = target.GetComponentInParent<FighterController>();
                worldCollider = null;
            }
            else
            {
                GrabbedFighter = null;
                worldCollider = best;
                worldAnchor = best.ClosestPoint(tip);
            }
        }

        /// <summary>
        /// 잡고 있던 고정 물체가 사라지거나 줄어들면(서든데스) 손을 놓는다. 매 스텝 호출.
        /// </summary>
        public void ValidateWorldGrip()
        {
            if (joint == null || worldCollider == null) return;
            if (!worldCollider || !worldCollider.enabled ||
                (worldCollider.ClosestPoint(worldAnchor) - worldAnchor).sqrMagnitude > 0.15f * 0.15f)
                Release();
        }

        static bool CanQueryClosestPoint(Collider c) =>
            !(c is MeshCollider mc) || mc.convex;

        public void Release()
        {
            if (joint != null)
            {
                if (Application.isPlaying) Destroy(joint);
                else DestroyImmediate(joint);
            }
            joint = null;
            worldCollider = null;
            GrabbedFighter = null;
        }

        void OnJointBreak(float force)
        {
            joint = null;
            worldCollider = null;
            GrabbedFighter = null;
        }
    }
}
