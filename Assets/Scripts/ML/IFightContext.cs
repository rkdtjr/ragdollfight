using UnityEngine;

namespace RagdollFight
{
    /// <summary>
    /// 에이전트가 관측하는 경기 정보. 학습 경기장(TrainingArena)과 실제 게임(ArenaGame)이 둘 다 제공한다.
    /// </summary>
    public interface IFightContext
    {
        Vector3 Center { get; }
        float BaseRadius { get; }
        float CurrentRadius { get; }
        float TimeLeft { get; }
        float RoundTime { get; }
    }

    public static class FightContextExtensions
    {
        /// <summary>경기장 가장자리까지 남은 거리 (밖이면 음수).</summary>
        public static float EdgeDistance(this IFightContext ctx, FighterController f)
        {
            Vector3 d = f.Body[Part.Hips].position - ctx.Center;
            return ctx.CurrentRadius - new Vector2(d.x, d.z).magnitude;
        }
    }
}
