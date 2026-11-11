using UnityEngine;

namespace RagdollFight
{
    /// <summary>
    /// 3인칭 카메라 (Human: Fall Flat 방식): P1 뒤를 따라가고 마우스로 시점을 돌린다.
    /// V 키로 경기장 전체를 내려다보는 관전 시점과 전환한다 (AI vs 봇 관전용).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class FightCamera : MonoBehaviour
    {
        public enum Mode { ThirdPerson, Overview }

        public Mode mode = Mode.ThirdPerson;

        [Header("Third person")]
        public float distance = 5f;
        public float pivotHeight = 1.1f;
        public float mouseSensitivity = 3f;
        public float minPitch = -30f;
        public float maxPitch = 60f;
        [Tooltip("래그돌 흔들림이 카메라에 그대로 전해지지 않게 따라가는 속도를 늦춘다")]
        public float followSmooth = 0.08f;

        [Header("Overview")]
        public Vector3 overviewOffset = new Vector3(0f, 8f, -9f);

        public Rigidbody target;            // 따라갈 대상 (P1 엉덩이)
        public System.Func<Vector3> overviewFocus;

        public float Yaw { get; private set; }
        float pitch = 18f;
        Vector3 pivot, pivotVel, overviewVel;
        const int ObstacleMask = ~(1 << Ragdoll.FighterLayer);

        /// <summary>조작 기준 회전 (수평 방향만). 키보드 이동을 이 방향 기준으로 바꾼다.</summary>
        public Quaternion ControlFrame => Quaternion.Euler(0f, Yaw, 0f);

        /// <summary>카메라 위아래 각도를 팔 높이(-1~1)로 바꾼다. 위를 보면 팔을 들고, 아래를 보면 내린다.</summary>
        /// 기본 시점(15°)에서 ±25° 기울이면 팔이 끝까지 올라가거나 내려간다.
        public float ArmPitch => Mathf.Clamp(-(pitch - 15f) / armPitchRange, -1f, 1f);

        [Tooltip("카메라를 이 각도만큼 기울이면 팔 높이가 끝까지 바뀐다 (작을수록 민감)")]
        public float armPitchRange = 25f;

        public void SnapBehind(Vector3 facing)
        {
            Yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            pitch = 18f;
            if (target != null) pivot = target.position + Vector3.up * pivotHeight;
            pivotVel = Vector3.zero;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.V))
                mode = mode == Mode.ThirdPerson ? Mode.Overview : Mode.ThirdPerson;

            // 3인칭에서는 커서를 숨기고 고정한다. Esc로 풀고, 클릭하면 다시 잠근다.
            if (mode == Mode.ThirdPerson)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) Cursor.lockState = CursorLockMode.None;
                else if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) Cursor.lockState = CursorLockMode.Locked;
            }
            else Cursor.lockState = CursorLockMode.None;
            Cursor.visible = Cursor.lockState != CursorLockMode.Locked;

            if (mode == Mode.ThirdPerson && Cursor.lockState == CursorLockMode.Locked)
            {
                Yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, minPitch, maxPitch);
            }
        }

        void LateUpdate()
        {
            if (mode == Mode.ThirdPerson && target != null)
            {
                Vector3 goal = target.position + Vector3.up * pivotHeight;
                pivot = Vector3.SmoothDamp(pivot, goal, ref pivotVel, followSmooth);

                Quaternion rot = Quaternion.Euler(pitch, Yaw, 0f);
                Vector3 back = rot * Vector3.back;
                float dist = distance;
                // 카메라가 바닥·벽을 뚫지 않게 당겨온다.
                if (Physics.SphereCast(pivot, 0.25f, back, out var hit, distance, ObstacleMask, QueryTriggerInteraction.Ignore))
                    dist = Mathf.Max(0.8f, hit.distance - 0.1f);
                transform.SetPositionAndRotation(pivot + back * dist, rot);
            }
            else
            {
                Vector3 focus = overviewFocus != null ? overviewFocus() : Vector3.zero;
                Vector3 desired = focus + overviewOffset;
                transform.position = Vector3.SmoothDamp(transform.position, desired, ref overviewVel, 0.4f);
                transform.LookAt(focus + Vector3.up * 0.5f);
                Yaw = 0f; // 관전 시점은 화면 위쪽 = +Z
            }
        }
    }
}
