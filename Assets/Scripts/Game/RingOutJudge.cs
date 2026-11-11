namespace RagdollFight
{
    /// <summary>
    /// 링아웃 판정. 한 명이 떨어져도 confirmWindow초 동안 더 지켜보고,
    /// 그 사이 다른 쪽도 떨어지면 "동시 낙하"로 본다 (상대를 붙잡고 같이 뛰어내리는 전략 방지).
    /// 실제 게임(ArenaGame)과 학습(TrainingArena)이 같은 규칙을 쓴다.
    /// </summary>
    public class RingOutJudge
    {
        public enum Outcome { None, P1Win, P2Win, MutualFall }

        public float confirmWindow = 1f;
        float timer = -1f;

        /// <summary>한 명이 떨어져서 판정을 기다리는 중인지</summary>
        public bool Pending => timer >= 0f;

        public void Reset() => timer = -1f;

        /// <summary>매 물리 스텝 호출. 결과가 확정되기 전에는 None.</summary>
        public Outcome Update(bool out1, bool out2, float dt)
        {
            if (out1 && out2) return Outcome.MutualFall;
            if (!out1 && !out2) { timer = -1f; return Outcome.None; }
            if (timer < 0f) timer = confirmWindow;
            timer -= dt;
            if (timer > 0f) return Outcome.None;
            return out1 ? Outcome.P2Win : Outcome.P1Win;
        }
    }
}
