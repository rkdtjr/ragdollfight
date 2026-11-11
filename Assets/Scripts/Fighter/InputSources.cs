using UnityEngine;

namespace RagdollFight
{
    /// <summary>키보드·마우스 입력. 프로젝트가 구버전 Input Manager를 쓰도록 설정돼 있다.</summary>
    public class KeyboardInputSource : IFighterInputSource
    {
        public enum Scheme
        {
            /// <summary>WASD 이동 · 스페이스 점프 · 마우스 좌/우 클릭 잡기 (발표 자료 조작)</summary>
            WasdMouse,
            /// <summary>방향키 이동 · 오른쪽 Shift 점프 · , / . 잡기 (2인 테스트용)</summary>
            Arrows
        }

        readonly Scheme scheme;
        readonly FightCamera cam;

        /// <param name="cam">이동·조준 기준 카메라. null이면 월드 기준이고 조준 없음.</param>
        public KeyboardInputSource(Scheme scheme, FightCamera cam = null)
        {
            this.scheme = scheme;
            this.cam = cam;
        }

        public FighterInput ReadInput()
        {
            var i = new FighterInput();
            float x, y;
            if (scheme == Scheme.WasdMouse)
            {
                x = Axis(KeyCode.D, KeyCode.A);
                y = Axis(KeyCode.W, KeyCode.S);
                i.grabLeft = Input.GetMouseButton(0);
                i.grabRight = Input.GetMouseButton(1);
                i.jump = Input.GetKey(KeyCode.Space);
            }
            else
            {
                x = Axis(KeyCode.RightArrow, KeyCode.LeftArrow);
                y = Axis(KeyCode.UpArrow, KeyCode.DownArrow);
                i.grabLeft = Input.GetKey(KeyCode.Comma);
                i.grabRight = Input.GetKey(KeyCode.Period);
                i.jump = Input.GetKey(KeyCode.RightShift);
            }
            // AI와 같은 9방향 해상도로 맞춘 뒤, 카메라 기준 방향을 월드 방향으로 바꾼다.
            Vector2 local = FighterInput.MoveFromIndex(FighterInput.IndexFromMove(new Vector2(x, y)));
            Quaternion frame = cam != null ? cam.ControlFrame : Quaternion.identity;
            Vector3 world = frame * new Vector3(local.x, 0f, local.y);
            i.move = new Vector2(world.x, world.z);

            // P1 3인칭: 카메라가 보는 방향 = 몸 방향, 카메라 위아래 = 팔 높이 (Human: Fall Flat 방식)
            if (scheme == Scheme.WasdMouse && cam != null && cam.mode == FightCamera.Mode.ThirdPerson)
            {
                Vector3 fwd = frame * Vector3.forward;
                i.aim = new Vector2(fwd.x, fwd.z);
                i.armPitch = cam.ArmPitch;
            }
            return i;
        }

        static float Axis(KeyCode pos, KeyCode neg) =>
            (Input.GetKey(pos) ? 1f : 0f) - (Input.GetKey(neg) ? 1f : 0f);
    }

    /// <summary>
    /// Rush Bot: 상대에게 계속 다가가고, 가까우면 양손으로 잡는다.
    /// 사람처럼 반응이 조금 늦도록 일정 간격으로만 판단을 갱신한다.
    /// </summary>
    public class RushBotInput : IFighterInputSource
    {
        readonly FighterController self;
        readonly FighterController target;
        public float grabDistance = 1.3f;
        /// <summary>이동 속도 배율 (난이도 조절용, 1 = 사람과 같은 속도)</summary>
        public float speedScale = 0.85f;
        /// <summary>판단을 갱신하는 간격 (물리 스텝 수). 0.01초 스텝 기준 20 = 0.2초 반응 지연.</summary>
        public int thinkEverySteps = 20;
        /// <summary>엉덩이가 이 높이 아래로 내려가면(경기장 밖으로 떨어지는 중) 손을 놓는다. 사람처럼.</summary>
        public float releaseBelowY = -0.2f;

        FighterInput decision;
        int stepsUntilThink;

        public RushBotInput(FighterController self, FighterController target)
        {
            this.self = self;
            this.target = target;
        }

        public FighterInput ReadInput()
        {
            if (self.Body[Part.Hips].position.y < releaseBelowY)
            {
                decision.grabLeft = decision.grabRight = false;
                return decision;
            }
            if (--stepsUntilThink > 0) return decision;
            stepsUntilThink = thinkEverySteps;

            var i = new FighterInput();
            Vector3 d = target.Body[Part.Chest].position - self.Body[Part.Chest].position;
            d.y = 0f;
            i.move = FighterInput.MoveFromIndex(FighterInput.IndexFromMove(new Vector2(d.x, d.z))) * speedScale;
            i.aim = new Vector2(d.x, d.z);
            bool close = d.magnitude < grabDistance;
            i.grabLeft = close;
            i.grabRight = close;
            decision = i;
            return i;
        }
    }

    /// <summary>아무것도 안 하는 상대 (조작감 테스트용).</summary>
    public class IdleInput : IFighterInputSource
    {
        public FighterInput ReadInput() => default;
    }
}
