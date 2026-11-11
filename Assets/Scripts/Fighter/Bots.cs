using UnityEngine;

namespace RagdollFight
{
    /// <summary>
    /// 랜덤 봇: 0.3~0.8초마다 아무 행동이나 고른다. (학습 상대 다양화용)
    /// </summary>
    public class RandomBotInput : IFighterInputSource
    {
        readonly FighterController self;
        FighterInput decision;
        float timer;

        public RandomBotInput(FighterController self) { this.self = self; }

        public FighterInput ReadInput()
        {
            timer -= Time.fixedDeltaTime;
            if (timer > 0f) return decision;
            timer = Random.Range(0.3f, 0.8f);
            var i = new FighterInput();
            i.move = FighterInput.MoveFromIndex(Random.Range(0, 9));
            bool grab = Random.value < 0.4f;
            i.grabLeft = grab && Random.value < 0.8f;
            i.grabRight = grab && Random.value < 0.8f;
            i.jump = Random.value < 0.05f;
            i.armPitch = Random.Range(-1, 2);
            decision = i;
            return i;
        }
    }

    /// <summary>
    /// 잡기 봇: 다가가서 잡고, 상대를 가장자리 쪽으로 끌고 간 뒤
    /// 상대가 가장자리 근처에 오면 놓고 가운데로 물러난다 (같이 떨어지지 않는다).
    /// </summary>
    public class GrabBotInput : IFighterInputSource
    {
        readonly FighterController self, target;
        readonly IFightContext ctx;
        public float grabDistance = 1.3f;
        public float releaseEdgeDistance = 0.9f;
        public float retreatTime = 0.7f;
        public float speedScale = 0.85f;
        public int thinkEverySteps = 20;

        FighterInput decision;
        int stepsUntilThink;
        float retreatTimer;

        public GrabBotInput(FighterController self, FighterController target, IFightContext ctx)
        {
            this.self = self;
            this.target = target;
            this.ctx = ctx;
        }

        public FighterInput ReadInput()
        {
            retreatTimer -= Time.fixedDeltaTime;
            if (self.Body[Part.Hips].position.y < ctx.Center.y - 0.2f)
            {
                decision.grabLeft = decision.grabRight = false;
                return decision;
            }
            if (--stepsUntilThink > 0) return decision;
            stepsUntilThink = thinkEverySteps;

            Vector3 me = self.Body[Part.Hips].position - ctx.Center;
            Vector3 them = target.Body[Part.Hips].position - ctx.Center;
            me.y = them.y = 0f;
            Vector3 toThem = them - me;
            var i = new FighterInput();
            bool holding = self.LeftHand.GrabbedFighter == target || self.RightHand.GrabbedFighter == target;

            if (retreatTimer > 0f)
            {
                // 놓고 가운데로 물러나기
                i.move = Flat(-me) * speedScale;
            }
            else if (holding && ctx.CurrentRadius - them.magnitude < releaseEdgeDistance)
            {
                retreatTimer = retreatTime;
                i.move = Flat(-me) * speedScale;
            }
            else if (holding)
            {
                // 상대를 바깥쪽으로 밀어붙이기
                Vector3 outward = them.sqrMagnitude > 0.01f ? them.normalized : toThem.normalized;
                i.move = Flat(toThem.normalized + outward) * speedScale;
                i.aim = new Vector2(toThem.x, toThem.z);
                i.grabLeft = i.grabRight = true;
            }
            else
            {
                i.move = Flat(toThem) * speedScale;
                i.aim = new Vector2(toThem.x, toThem.z);
                bool close = toThem.magnitude < grabDistance;
                i.grabLeft = i.grabRight = close;
            }
            decision = i;
            return i;
        }

        static Vector2 Flat(Vector3 v) => FighterInput.MoveFromIndex(FighterInput.IndexFromMove(new Vector2(v.x, v.z)));
    }
}
