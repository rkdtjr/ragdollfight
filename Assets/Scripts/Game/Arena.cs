using UnityEngine;

namespace RagdollFight
{
    /// <summary>
    /// 원형 경기장. 서든데스 때 반지름을 줄일 수 있다.
    /// 플레이(ArenaGame)·시뮬레이션·학습 환경이 같은 경기장을 쓰도록 따로 뺐다.
    /// </summary>
    public class Arena : MonoBehaviour
    {
        public float BaseRadius { get; private set; }
        public float CurrentRadius { get; private set; }

        Transform platform, rim;

        public static Arena Create(Transform parent, float radius)
        {
            var go = new GameObject("Arena");
            go.transform.SetParent(parent, false);
            var arena = go.AddComponent<Arena>();
            arena.Build(radius);
            return arena;
        }

        void Build(float radius)
        {
            BaseRadius = radius;

            var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            p.name = "Platform";
            p.transform.SetParent(transform, false);
            p.transform.localPosition = new Vector3(0f, -0.5f, 0f); // 윗면 y = 0
            // 원기둥 기본 콜라이더는 캡슐이라 모서리가 둥글다 → 실제 모양(메시)으로 바꾼다.
            DestroyImmediate(p.GetComponent<Collider>());
            var mc = p.AddComponent<MeshCollider>();
            mc.sharedMesh = p.GetComponent<MeshFilter>().sharedMesh;
            mc.convex = true; // 손으로 잡은 지점 계산(ClosestPoint)에 필요
            mc.sharedMaterial = new PhysicsMaterial("Arena") { dynamicFriction = 0.6f, staticFriction = 0.6f };
            Tint(p, new Color(0.55f, 0.55f, 0.6f));
            platform = p.transform;

            // 경계 표시용 테두리 (충돌 없음)
            var r = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            r.name = "Rim";
            r.transform.SetParent(transform, false);
            r.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            DestroyImmediate(r.GetComponent<Collider>());
            Tint(r, new Color(0.9f, 0.75f, 0.2f));
            rim = r.transform;

            SetRadius(radius);
        }

        public void SetRadius(float radius)
        {
            CurrentRadius = radius;
            platform.localScale = new Vector3(radius * 2f, 0.5f, radius * 2f);
            rim.localScale = new Vector3(radius * 2f + 0.15f, 0.48f, radius * 2f + 0.15f);
        }

        /// <summary>
        /// 서든데스: 남은 시간이 suddenDeathTime보다 적어지면 반지름을 minRadius까지 선형으로 줄인다.
        /// </summary>
        public void UpdateSuddenDeath(float timeLeft, float suddenDeathTime, float minRadius)
        {
            float t = suddenDeathTime > 0f ? Mathf.Clamp01(timeLeft / suddenDeathTime) : 1f;
            float r = Mathf.Lerp(minRadius, BaseRadius, t);
            if (!Mathf.Approximately(r, CurrentRadius)) SetRadius(r);
        }

        /// <summary>
        /// 시간 종료 판정: 경기장 가운데에 더 가까운 쪽이 이긴다. 차이가 margin 안이면 무승부.
        /// </summary>
        /// <returns>+1 = a 승, -1 = b 승, 0 = 무승부</returns>
        public static int JudgeTimeout(Vector3 a, Vector3 b, float margin = 0.1f)
        {
            float da = new Vector2(a.x, a.z).magnitude;
            float db = new Vector2(b.x, b.z).magnitude;
            if (Mathf.Abs(da - db) < margin) return 0;
            return da < db ? 1 : -1;
        }

        static void Tint(GameObject go, Color color)
        {
            var rend = go.GetComponent<Renderer>();
            var m = new Material(rend.sharedMaterial);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            rend.sharedMaterial = m;
        }
    }
}
