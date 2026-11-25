using UnityEngine;

namespace RagdollFight
{
    /// <summary>
    /// 평가 전용 FSM 봇. 학습 때는 한 번도 나오지 않는다 ("처음 보는 상대" 평가용).
    /// 러쉬(돌진)·잡기(밀고 놓기)·회피를 상황에 따라 섞는다.
    ///
    /// 상태: 접근 → (견제) → 잡기 → 밀기 → 들어올리기 → 놓고 물러나기
    ///       위험할 때: 피하기(가장자리에서 돌진해 오면 옆으로), 탈출(잡혔을 때), 복귀(내가 가장자리)
    /// </summary>
    public class FsmBotInput : IFighterInputSource
    {
        public enum State { Approach, Strafe, Grab, Push, Lift, Retreat, Dodge, Escape, Recover }

        readonly FighterController self, target;
        readonly IFightContext ctx;

        public float thinkInterval = 0.15f;
        public float speedScale = 0.9f;
        public float grabDistance = 1.25f;
        public float liftEdgeDistance = 1.3f;     // 상대가 가장자리에서 이만큼 안이면 들어올린다
        public float releaseEdgeDistance = 0.7f;  // 이만큼 안이면 놓고 물러난다
        public float dangerEdgeDistance = 1.2f;   // 내가 가장자리에서 이만큼 안이면 위험

        public State Current { get; private set; } = State.Approach;

        FighterInput decision;
        float thinkTimer, stateTimer;
        int strafeDir = 1;

        public FsmBotInput(FighterController self, FighterController target, IFightContext ctx)
        {
            this.self = self;
            this.target = target;
            this.ctx = ctx;
        }

        public FighterInput ReadInput()
        {
            float dt = Time.fixedDeltaTime;
            stateTimer -= dt;
            // 떨어지는 중이면 손을 놓는다 (같이 떨어지지 않게)
            if (self.Body[Part.Hips].position.y < ctx.Center.y - 0.2f)
            {
                decision.grabLeft = decision.grabRight = false;
                return decision;
            }
            thinkTimer -= dt;
            if (thinkTimer > 0f) return decision;
            thinkTimer = thinkInterval;

            Vector3 me = Flat(self.Body[Part.Hips].position - ctx.Center);
            Vector3 them = Flat(target.Body[Part.Hips].position - ctx.Center);
            Vector3 toThem = them - me;
            float dist = toThem.magnitude;
            Vector3 dirToThem = dist > 0.01f ? toThem / dist : Vector3.forward;
            float myEdge = ctx.CurrentRadius - me.magnitude;
            float theirEdge = ctx.CurrentRadius - them.magnitude;
            bool holding = self.LeftHand.GrabbedFighter == target || self.RightHand.GrabbedFighter == target;
            bool held = target.LeftHand.GrabbedFighter == self || target.RightHand.GrabbedFighter == self;
            Vector3 theirVel = Flat(target.Body[Part.Hips].linearVelocity);
            float closing = Vector3.Dot(theirVel, -dirToThem); // 상대가 나에게 다가오는 속도

            // ── 상태 전이 (위험 상황이 우선) ──
            if (held && !holding && myEdge < dangerEdgeDistance) Go(State.Escape, 0.5f);
            else if (!holding && myEdge < dangerEdgeDistance && dist < 2.2f && closing > 1.2f && Current != State.Dodge)
                Go(State.Dodge, 0.6f);
            else if (!holding && myEdge < 0.8f && Current != State.Dodge && Current != State.Escape) Go(State.Recover, 0.6f);
            else if (stateTimer <= 0f || Current == State.Approach || Current == State.Grab || Current == State.Push || Current == State.Lift)
            {
                if (Current == State.Retreat && stateTimer > 0f) { }
                else if (holding && theirEdge < releaseEdgeDistance) Go(State.Retreat, Random.Range(0.6f, 1.0f));
                else if (holding && theirEdge < liftEdgeDistance) Go(State.Lift, 0f);
                else if (holding) Go(State.Push, 0f);
                else if (self.IsExhausted) Go(State.Strafe, 0.8f);
                else if (dist < grabDistance) Go(State.Grab, 0f);
                else if (dist < 2.2f && Current == State.Approach && Random.value < 0.25f)
                {
                    strafeDir = Random.value < 0.5f ? 1 : -1;
                    Go(State.Strafe, Random.Range(0.4f, 1.0f));
                }
                else if (Current != State.Strafe || stateTimer <= 0f) Go(State.Approach, 0f);
            }

            // ── 행동 ──
            var i = new FighterInput { aim = new Vector2(dirToThem.x, dirToThem.z) };
            Vector3 side = Vector3.Cross(Vector3.up, dirToThem) * strafeDir;
            Vector3 toCenter = me.sqrMagnitude > 0.01f ? -me.normalized : Vector3.zero;
            switch (Current)
            {
                case State.Approach:
                    i.move = Move(dirToThem);
                    break;
                case State.Strafe:
                    i.move = Move(side * 0.8f + dirToThem * 0.2f);
                    break;
                case State.Grab:
                    i.move = Move(dirToThem);
                    i.grabLeft = i.grabRight = true;
                    break;
                case State.Push:
                case State.Lift:
                {
                    Vector3 outward = them.sqrMagnitude > 0.01f ? them.normalized : dirToThem;
                    i.move = Move(outward + dirToThem * 0.5f);
                    i.grabLeft = i.grabRight = true;
                    if (Current == State.Lift) i.armPitch = 1f;
                    break;
                }
                case State.Retreat:
                case State.Recover:
                    i.move = Move(toCenter);
                    i.aim = Vector2.zero;
                    break;
                case State.Dodge:
                {
                    // 가장자리와 나란히(바깥 반대쪽 성분 포함) 옆으로 비켜선다
                    Vector3 tangent = Vector3.Cross(Vector3.up, toCenter);
                    if (Vector3.Dot(tangent, side) < 0f) tangent = -tangent;
                    i.move = Move(tangent + toCenter * 0.4f);
                    break;
                }
                case State.Escape:
                    // 상대 반대쪽 + 안쪽으로 빠져나간다 (잡기 버튼을 놓는다)
                    i.move = Move(-dirToThem * 0.5f + side + toCenter);
                    break;
            }
            decision = i;
            return i;
        }

        void Go(State s, float duration)
        {
            if (Current != s) stateTimer = duration;
            Current = s;
        }

        Vector2 Move(Vector3 v)
        {
            var m = FighterInput.MoveFromIndex(FighterInput.IndexFromMove(new Vector2(v.x, v.z)));
            return m * speedScale;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
