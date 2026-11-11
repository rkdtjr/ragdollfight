using UnityEngine;

namespace RagdollFight
{
    /// <summary>
    /// Active Ragdoll 제어기. 균형·걷기·팔 뻗기·점프·기절을 규칙으로 처리한다.
    /// AI(또는 사람·봇)는 FighterInput(이동·잡기·점프)만 넘겨주면 된다.
    ///
    /// 균형은 Gang Beasts / Human: Fall Flat처럼 "외부 힘"으로 잡는다:
    ///  1) 엉덩이 호버 스프링: 바닥까지 거리를 재서 엉덩이를 일정 높이로 띄운다.
    ///  2) 직립 토크: 엉덩이·가슴을 바라보는 방향으로 세운다.
    ///  3) 머리 리프트: 머리 무게만큼 위로 당겨 고개가 처지지 않게 한다.
    /// 팔다리 관절 힘은 약하게 두어 흐물거리는 느낌을 낸다.
    /// </summary>
    [RequireComponent(typeof(Ragdoll))]
    public class FighterController : MonoBehaviour
    {
        [Header("Balance (가속도 단위)")]
        public float hoverSpring = 120f;
        public float hoverDamper = 14f;
        public float uprightSpring = 600f;
        public float uprightDamper = 40f;
        public float chestUprightSpring = 400f;
        public float chestUprightDamper = 25f;
        [Tooltip("머리 무게 대비 위로 당기는 힘 배율")]
        public float headLift = 1.0f;

        [Header("Movement")]
        public float moveSpeed = 2.5f;
        public float moveAccel = 12f;
        public float maxAccel = 30f;
        [Range(0, 1)] public float airControl = 0.3f;
        [Tooltip("초당 회전 각도 (라디안)")]
        public float turnSpeed = 8f;
        public float strideLength = 1.0f;
        public float jumpSpeed = 4f;
        public float jumpCooldown = 1.2f;
        [Tooltip("점프 한 번에 줄어드는 악력 (마구 뛰는 것 방지)")]
        public float jumpStaminaCost = 0.15f;

        [Header("Arms")]
        [Tooltip("armPitch = 0일 때 팔 각도 (90 = 수평)")]
        public float reachPitch = 95f;
        [Tooltip("armPitch = ±1일 때 위아래로 더 움직이는 각도")]
        public float reachPitchRange = 65f;
        public float armIdleStrength = 0.08f;
        public float armReachStrength = 1.6f;

        [Header("Crouch (팔을 아래로 뻗을 때)")]
        [Tooltip("armPitch = -1일 때 엉덩이를 낮추는 높이")]
        public float crouchDepth = 0.35f;
        public float crouchChestBend = 55f;
        public float crouchHipBend = 20f;

        [Header("Lift (잡고 팔을 들면 상대를 들어올림)")]
        [Tooltip("armPitch가 이 값 이상이면 잡은 상대를 들어올린다")]
        public float liftPitchThreshold = 0.4f;
        [Tooltip("들린 쪽의 균형(버티기·이동) 힘 배율")]
        public float liftBalanceFactor = 0.15f;
        [Tooltip("들린 쪽의 직립 힘 배율 (너무 낮으면 들리자마자 옆으로 눕는다)")]
        public float liftUprightFactor = 0.6f;
        [Tooltip("드는 쪽 엉덩이 높이 기준으로 상대 엉덩이를 이만큼까지만 들어올린다")]
        public float liftHeight = 0.45f;
        [Tooltip("상대를 잡고 있을 때 직립 힘 배율 (무게에 끌려 숙여지지 않게 버틴다)")]
        public float holdBraceFactor = 2f;
        [Tooltip("손 하나당 들린 상대 몸무게 대비 위로 받쳐주는 힘")]
        public float liftAssistPerHand = 0.45f;
        [Tooltip("들고 있을 때 손 하나당 추가로 줄어드는 악력 (초당)")]
        public float liftExtraDrainPerHand = 0.03f;
        [Tooltip("들린 상대가 버둥거리면(이동 입력) 추가로 줄어드는 악력 (초당)")]
        public float struggleDrain = 0.1f;

        [Header("Hang & climb (가장자리 매달리기)")]
        public float climbUpSpeed = 6f;
        public float climbForwardSpeed = 2.5f;
        public float climbStaminaCost = 0.3f;

        [Header("Grip stamina (악력)")]
        [Tooltip("손 하나로 잡고 있을 때 초당 줄어드는 악력 (양손이면 2배)")]
        public float gripDrainPerHand = 0.12f;
        [Tooltip("잡지 않을 때 초당 회복량")]
        public float gripRegen = 0.35f;
        [Tooltip("악력이 바닥난 뒤 이만큼 회복해야 다시 잡을 수 있다")]
        public float gripRecoverThreshold = 0.35f;

        [Header("Knockout")]
        [Tooltip("머리에 이 속도 이상으로 상대 몸이 부딪히면 기절")]
        public float koImpactSpeed = 7f;
        public float koDuration = 1.2f;
        public float recoverTime = 0.6f;

        [Tooltip("끄면 FixedUpdate에서 자동으로 돌지 않는다 (테스트·외부 스텝용)")]
        public bool autoStep = true;

        public IFighterInputSource InputSource { get; set; }
        public FighterInput LastInput { get; private set; }
        public Ragdoll Body { get; private set; }
        public HandGrabber LeftHand { get; private set; }
        public HandGrabber RightHand { get; private set; }
        public bool IsGrounded { get; private set; }
        /// <summary>1 = 정상, 0 = 기절(완전히 흐물). 균형·근육 힘에 곱해진다.</summary>
        public float Consciousness { get; private set; } = 1f;
        public Vector3 Facing => facing;
        public bool IsKnockedOut => koTimer > 0f;
        /// <summary>악력 0~1. 잡고 있으면 줄고, 0이 되면 손이 풀린다.</summary>
        public float Stamina { get; private set; } = 1f;
        /// <summary>악력이 바닥나서 다시 잡을 수 없는 상태</summary>
        public bool IsExhausted { get; private set; }

        /// <summary>상대에게 들려 있는 상태 (균형 힘이 약해진다)</summary>
        public bool IsLifted => liftedTimer > 0f;
        /// <summary>바닥·가장자리를 잡고 공중에 매달린 상태</summary>
        public bool IsHanging { get; private set; }

        Vector3 facing = Vector3.forward;
        float liftedTimer;
        float grabBlockTimer;
        // 기어오르기 2단계: 먼저 위로 솟고, 가장자리 높이를 넘으면 안쪽으로 밀어준다
        float climbTimer;
        float climbBoostTimer;
        float climbLedgeY;
        Vector3 climbDir;
        float walkPhase;
        float jumpTimer;
        float koTimer;
        const int GroundMask = ~(1 << Ragdoll.FighterLayer);

        /// <summary>Ragdoll을 만든 직후 한 번 호출한다.</summary>
        public void Init()
        {
            Body = GetComponent<Ragdoll>();
            LeftHand = Body[Part.LowerArmL].gameObject.AddComponent<HandGrabber>();
            RightHand = Body[Part.LowerArmR].gameObject.AddComponent<HandGrabber>();
            LeftHand.owner = this;
            RightHand.owner = this;
            var head = Body[Part.Head].gameObject.AddComponent<HeadImpact>();
            head.owner = this;
            facing = Flat(transform.forward);
        }

        void FixedUpdate()
        {
            if (autoStep && Body != null) Step(Time.fixedDeltaTime);
        }

        public void Step(float dt)
        {
            var input = InputSource != null ? InputSource.ReadInput() : default;
            LastInput = input;

            // ── 의식(기절) ──
            if (koTimer > 0f) { koTimer -= dt; Consciousness = 0f; }
            else Consciousness = Mathf.MoveTowards(Consciousness, 1f, dt / recoverTime);
            float c = Consciousness;
            if (liftedTimer > 0f) liftedTimer -= dt;
            if (grabBlockTimer > 0f) grabBlockTimer -= dt;
            // 균형 힘 배율: 기절하면 0, 들려 있으면 크게 약해진다
            float b = c * (IsLifted ? liftBalanceFactor : 1f);


            var hips = Body[Part.Hips];
            var chest = Body[Part.Chest];
            var head = Body[Part.Head];
            float g = -Physics.gravity.y;

            // ── 1) 엉덩이 호버 스프링 ──
            IsGrounded = Physics.Raycast(hips.position, Vector3.down, out var hit,
                Body.RestHipHeight + 0.35f, GroundMask, QueryTriggerInteraction.Ignore);

            // 땅에 서서 팔을 아래로 뻗으면 몸을 숙이고 무릎을 굽힌다 (바닥·쓰러진 상대 잡기)
            bool reachingAny = (input.grabLeft || input.grabRight) && !IsExhausted && c > 0.5f;
            float down = reachingAny && IsGrounded ? Mathf.Clamp01(-input.armPitch) : 0f;
            if (IsGrounded && b > 0f)
            {
                float targetHeight = Body.RestHipHeight - crouchDepth * down;
                float err = targetHeight - hit.distance;
                float accel = g + hoverSpring * err - hoverDamper * hips.linearVelocity.y;
                accel = Mathf.Clamp(accel, 0f, 3f * g);
                hips.AddForce(Vector3.up * (accel * Body.TotalMass * b), ForceMode.Force);
            }

            // ── 바라보는 방향 ──
            Vector3 moveWorld = Vector3.ClampMagnitude(new Vector3(input.move.x, 0f, input.move.y), 1f);
            // 조준 방향이 있으면 그쪽을, 없으면 이동 방향을 바라본다.
            Vector3 aimWorld = new Vector3(input.aim.x, 0f, input.aim.y);
            Vector3 lookDir = aimWorld.sqrMagnitude > 0.01f ? aimWorld : moveWorld;
            if (lookDir.sqrMagnitude > 0.01f && c > 0.3f)
                facing = Vector3.RotateTowards(facing, lookDir.normalized, turnSpeed * dt, 0f);
            facing = Flat(facing);

            // ── 2) 직립 토크 ──
            Quaternion targetRot = Quaternion.LookRotation(facing, Vector3.up);
            float dampScale = Mathf.Max(b, 0.3f);
            // 들려 있어도 몸통은 어느 정도 세운다. 상대를 잡고 있으면 그 무게에 숙여지지 않게 더 세게 버틴다.
            float bu = c * (IsLifted ? liftUprightFactor : 1f);
            bool holdingFighter = LeftHand.GrabbedFighter != null || RightHand.GrabbedFighter != null;
            float brace = holdingFighter && !IsLifted ? holdBraceFactor : 1f;
            dampScale = Mathf.Max(bu, 0.3f) * Mathf.Sqrt(brace);
            ApplyOrientationPD(hips, targetRot * Quaternion.Euler(crouchHipBend * down, 0f, 0f), uprightSpring * bu * brace, uprightDamper * dampScale);
            ApplyOrientationPD(chest, targetRot * Quaternion.Euler(crouchChestBend * down, 0f, 0f), chestUprightSpring * bu * brace, chestUprightDamper * dampScale);

            // ── 3) 머리 리프트 ──
            head.AddForce(Vector3.up * (g * head.mass * headLift * c), ForceMode.Force);

            // ── 이동 ──
            Vector3 vel = hips.linearVelocity;
            Vector3 velH = new Vector3(vel.x, 0f, vel.z);
            Vector3 acc = Vector3.ClampMagnitude((moveWorld * moveSpeed - velH) * moveAccel, maxAccel);
            // 기어오르는 중에는 공중 제동을 꺼서 가장자리 안쪽으로 날아가게 한다
            float control = (IsGrounded ? 1f : (climbBoostTimer > 0f ? 0f : airControl)) * b;
            hips.AddForce(acc * (Body.TotalMass * 0.6f * control), ForceMode.Force);
            chest.AddForce(acc * (Body.TotalMass * 0.4f * control), ForceMode.Force);

            // ── 다리: 절차적 걷기 ──
            float speed = velH.magnitude;
            float speed01 = Mathf.Clamp01(speed / moveSpeed);
            walkPhase = (walkPhase + dt * speed * Mathf.PI * 2f / strideLength) % (Mathf.PI * 2f);
            float swing = Mathf.Sin(walkPhase);
            float lift = Mathf.Cos(walkPhase);
            if (IsGrounded)
            {
                float amp = 45f * speed01;
                float crouchThigh = -60f * down, crouchKnee = 100f * down;
                SetLeg(Part.UpperLegL, Part.LowerLegL, crouchThigh - amp * swing, crouchKnee + amp * 1.3f * Mathf.Max(0f, lift), c);
                SetLeg(Part.UpperLegR, Part.LowerLegR, crouchThigh + amp * swing, crouchKnee + amp * 1.3f * Mathf.Max(0f, -lift), c);
            }
            else
            {
                // 공중: 다리를 살짝 접는다
                SetLeg(Part.UpperLegL, Part.LowerLegL, -30f, 60f, c);
                SetLeg(Part.UpperLegR, Part.LowerLegR, -30f, 60f, c);
            }

            // ── 몸통 ──
            Body.SetDrive(Part.Chest, Quaternion.Euler((crouchChestBend - crouchHipBend) * down, 0f, 0f), c);
            Body.SetDrive(Part.Head, Quaternion.identity, c);

            // ── 팔: 잡기 버튼 = 팔을 앞으로 뻗기 (Human: Fall Flat 방식) ──
            float armSwing = 25f * speed01 * swing;
            UpdateStamina(dt);
            float pitch = reachPitch + Mathf.Clamp(input.armPitch, -1f, 1f) * reachPitchRange;
            bool canGrab = !IsExhausted && grabBlockTimer <= 0f;
            SetArm(Part.UpperArmL, Part.LowerArmL, LeftHand, input.grabLeft && canGrab, pitch, armSwing, c);
            SetArm(Part.UpperArmR, Part.LowerArmR, RightHand, input.grabRight && canGrab, pitch, -armSwing, c);
            LeftHand.ValidateWorldGrip();
            RightHand.ValidateWorldGrip();

            // ── 들어올리기: 상대를 잡은 채 팔을 위로 ──
            if (input.armPitch >= liftPitchThreshold && c > 0.5f)
            {
                bool struggling = false;
                int liftingHands = LiftWith(LeftHand, g, ref struggling) + LiftWith(RightHand, g, ref struggling);
                float extra = liftExtraDrainPerHand * liftingHands + (struggling ? struggleDrain : 0f);
                Stamina = Mathf.Clamp01(Stamina - extra * dt);
            }

            IsHanging = !IsGrounded && (LeftHand.IsGrabbingWorld || RightHand.IsGrabbingWorld);

            if (climbBoostTimer > 0f) climbBoostTimer = IsGrounded ? 0f : climbBoostTimer - dt;
            if (climbTimer > 0f)
            {
                climbTimer -= dt;
                if (hips.position.y > climbLedgeY + 0.3f)
                {
                    foreach (var rb in Body.bodies) rb.AddForce(climbDir * climbForwardSpeed, ForceMode.VelocityChange);
                    climbTimer = 0f;
                    climbBoostTimer = 0.6f;
                }
            }

            // ── 점프 ──
            jumpTimer -= dt;
            if (input.jump && jumpTimer <= 0f && c > 0.8f && !IsLifted)
            {
                if (IsGrounded && Stamina >= jumpStaminaCost)
                {
                    foreach (var rb in Body.bodies) rb.AddForce(Vector3.up * jumpSpeed, ForceMode.VelocityChange);
                    jumpTimer = jumpCooldown;
                    Stamina -= jumpStaminaCost;
                }
                else if (IsHanging && Stamina > 0.05f)
                {
                    // 기어오르기: 잡은 지점 쪽 위로 몸을 튕겨 올린다
                    Vector3 anchor = LeftHand.IsGrabbingWorld ? LeftHand.WorldAnchor : RightHand.WorldAnchor;
                    Vector3 toAnchor = anchor - hips.position;
                    toAnchor.y = 0f;
                    climbDir = toAnchor.sqrMagnitude > 1e-4f ? toAnchor.normalized : facing;
                    climbLedgeY = anchor.y;
                    climbTimer = 1f;
                    climbBoostTimer = 1f;
                    LeftHand.Release();
                    RightHand.Release();
                    grabBlockTimer = 0.35f; // 올라가는 도중 가장자리를 다시 잡지 않게
                    // 1단계: 벽에서 살짝 떨어지며 위로 솟는다 (안쪽으로 미는 건 가장자리를 넘은 뒤)
                    Vector3 v = Vector3.up * climbUpSpeed - climbDir * 0.3f;
                    foreach (var rb in Body.bodies) rb.AddForce(v - rb.linearVelocity, ForceMode.VelocityChange);
                    Stamina = Mathf.Clamp01(Stamina - climbStaminaCost);
                    jumpTimer = jumpCooldown;
                }
            }
        }

        int LiftWith(HandGrabber hand, float g, ref bool struggling)
        {
            var victim = hand.GrabbedFighter;
            if (victim == null || victim == this) return 0;
            victim.liftedTimer = 0.06f;
            // 잡은 부위(팔 등)가 아니라 몸 중심을 받쳐 올린다 — 팔만 늘어나고 몸은 가라앉는 걸 막는다
            // 목표 높이(드는 쪽 엉덩이 + liftHeight)에 가까워질수록 받쳐주는 힘을 줄인다 — 머리 위로 치솟지 않게
            float targetY = Body[Part.Hips].position.y + liftHeight;
            float k = Mathf.Clamp01((targetY - victim.Body[Part.Hips].position.y) / 0.25f);
            Vector3 assist = Vector3.up * (victim.Body.TotalMass * g * liftAssistPerHand * k);
            victim.Body[Part.Hips].AddForce(assist * 0.5f, ForceMode.Force);
            victim.Body[Part.Chest].AddForce(assist * 0.5f, ForceMode.Force);
            if (victim.LastInput.move.sqrMagnitude > 0.25f) struggling = true;
            return 1;
        }

        void UpdateStamina(float dt)
        {
            int holding = (LeftHand.IsGrabbing ? 1 : 0) + (RightHand.IsGrabbing ? 1 : 0);
            if (holding > 0) Stamina -= gripDrainPerHand * holding * dt;
            else Stamina += gripRegen * dt;
            Stamina = Mathf.Clamp01(Stamina);

            if (Stamina <= 0f) IsExhausted = true;               // 바닥나면 손이 풀린다 (SetArm에서 reach = false)
            else if (IsExhausted && Stamina >= gripRecoverThreshold) IsExhausted = false;
        }

        void SetLeg(Part upper, Part lower, float thighPitch, float kneeBend, float strength)
        {
            // 허벅지: -X 회전 = 앞으로.  무릎: +X 회전 = 종아리가 뒤로 접힘.
            Body.SetDrive(upper, Quaternion.Euler(thighPitch, 0f, 0f), strength);
            Body.SetDrive(lower, Quaternion.Euler(kneeBend, 0f, 0f), strength);
        }

        void SetArm(Part upper, Part lower, HandGrabber hand, bool reach, float pitch, float idleSwing, float c)
        {
            hand.wantGrab = reach && c > 0.5f;
            if (!hand.wantGrab) hand.Release();
            else hand.TryGrabNearby();
            if (reach)
            {
                Body.SetDrive(upper, Quaternion.Euler(-pitch, 0f, 0f), armReachStrength * c);
                Body.SetDrive(lower, Quaternion.identity, armReachStrength * c);
            }
            else
            {
                Body.SetDrive(upper, Quaternion.Euler(idleSwing, 0f, 0f), armIdleStrength * c);
                Body.SetDrive(lower, Quaternion.Euler(-15f, 0f, 0f), armIdleStrength * c);
            }
        }

        static void ApplyOrientationPD(Rigidbody rb, Quaternion target, float kp, float kd)
        {
            Quaternion delta = target * Quaternion.Inverse(rb.rotation);
            if (delta.w < 0f) { delta.x = -delta.x; delta.y = -delta.y; delta.z = -delta.z; delta.w = -delta.w; }
            delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
            Vector3 torque = -rb.angularVelocity * kd;
            if (angleDeg > 0.01f && !float.IsInfinity(axis.x) && !float.IsNaN(axis.x))
                torque += axis.normalized * (angleDeg * Mathf.Deg2Rad * kp);
            rb.AddTorque(torque, ForceMode.Acceleration);
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        public void KnockOut(float duration = -1f)
        {
            koTimer = Mathf.Max(koTimer, duration > 0f ? duration : koDuration);
            LeftHand.Release();
            RightHand.Release();
        }

        public bool Owns(Rigidbody rb) => Body != null && Body.Owns(rb);

        /// <summary>라운드·에피소드 시작 시 자세·상태를 초기화한다.</summary>
        public void ResetFighter(Vector3 position, Quaternion rotation)
        {
            LeftHand.Release();
            RightHand.Release();
            Body.ResetPose(position, rotation);
            facing = Flat(rotation * Vector3.forward);
            Consciousness = 1f;
            Stamina = 1f;
            IsExhausted = false;
            liftedTimer = 0f;
            grabBlockTimer = 0f;
            climbTimer = 0f;
            climbBoostTimer = 0f;
            IsHanging = false;
            koTimer = 0f;
            jumpTimer = 0f;
            walkPhase = 0f;
        }
    }
}
