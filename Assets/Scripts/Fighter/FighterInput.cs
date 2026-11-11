using UnityEngine;

namespace RagdollFight
{
    /// <summary>
    /// 사람·봇·AI가 똑같이 쓰는 조작 입력 (발표 자료의 "공통 조작 계층").
    /// 이동 9방향 · 바라보는 방향 · 팔 높이 · 왼손 잡기 · 오른손 잡기 · 점프.
    /// </summary>
    public struct FighterInput
    {
        /// <summary>월드 기준 이동 방향. x = 오른쪽(+X), y = 앞(+Z). 길이 0~1.</summary>
        public Vector2 move;
        /// <summary>몸이 바라볼 월드 방향 (x, z). 0이면 이동 방향을 바라본다.</summary>
        public Vector2 aim;
        /// <summary>잡기 버튼을 누를 때 팔 높이. -1 = 아래(바닥 쪽), 0 = 수평, 1 = 위.</summary>
        public float armPitch;
        public bool grabLeft;
        public bool grabRight;
        public bool jump;

        /// <summary>ML-Agents 이산 행동(0 = 정지, 1~8 = 북쪽부터 시계방향 45도씩)을 이동 벡터로 바꾼다.</summary>
        public static Vector2 MoveFromIndex(int index)
        {
            if (index <= 0 || index > 8) return Vector2.zero;
            float a = (index - 1) * 45f * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
        }

        /// <summary>임의의 이동 벡터를 가장 가까운 9방향 인덱스로 바꾼다 (봇도 AI와 같은 해상도로 움직이게).</summary>
        public static int IndexFromMove(Vector2 move)
        {
            if (move.sqrMagnitude < 0.04f) return 0;
            float angle = Mathf.Atan2(move.x, move.y) * Mathf.Rad2Deg; // 0 = 앞, 시계방향 +
            if (angle < 0) angle += 360f;
            return Mathf.RoundToInt(angle / 45f) % 8 + 1;
        }
    }

    public interface IFighterInputSource
    {
        FighterInput ReadInput();
    }
}
