"""학습 로그(TensorBoard)에서 승률·보상 추이를 표로 뽑는다.

사용법:  uv run python summary.py pilot1
"""
import sys
from pathlib import Path

from tensorboard.backend.event_processing.event_accumulator import EventAccumulator

TAGS = [
    ("Environment/Cumulative Reward", "보상"),
    ("Match/Win", "승률"),
    ("Match/Loss", "패배율"),
    ("Match/RingOutWin", "링아웃승"),
    ("Match/MutualFall", "동시낙하"),
    ("Match/Win_vs_Rush", "승_러쉬"),
    ("Match/Win_vs_Grab", "승_잡기"),
    ("Match/Win_vs_Random", "승_랜덤"),
    ("Match/Length", "경기시간(s)"),
]


def main(run_id: str, results: str = "results") -> None:
    run_dir = Path(results) / run_id / "Fighter"
    acc = EventAccumulator(str(run_dir), size_guidance={"scalars": 0})
    acc.Reload()
    available = set(acc.Tags()["scalars"])
    series = {name: {e.step: e.value for e in acc.Scalars(tag)} for tag, name in TAGS if tag in available}
    steps = sorted(set().union(*[s.keys() for s in series.values()]))
    header = ["step"] + list(series)
    print(" | ".join(f"{h:>10}" for h in header))
    for st in steps:
        row = [f"{st:>10}"] + [f"{series[n].get(st, float('nan')):>10.3f}" for n in series]
        print(" | ".join(row))


if __name__ == "__main__":
    main(*sys.argv[1:])
