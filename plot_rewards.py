# List of runs (directories under 'results/')
import os
import json
import matplotlib.pyplot as plt

# Runs to compare

runs = ["ultimate_run10", "ultimate_run9","bc_run_4"]  # Add more if you have them
base_dir = "results"
behavior_to_plot = "RunnerBehavior"  # or "TaggerBehavior"

plt.figure(figsize=(12, 6))

for run in runs:
    path = os.path.join(base_dir, run, "run_logs", "training_status.json")
    if not os.path.exists(path):
        print(f"Skipping missing file: {path}")
        continue

    with open(path, "r") as f:
        data = json.load(f)

    if behavior_to_plot not in data or "checkpoints" not in data[behavior_to_plot]:
        print(f"No checkpoints found in {run} for behavior {behavior_to_plot}")
        continue

    checkpoints = data[behavior_to_plot]["checkpoints"]
    steps = []
    rewards = []

    for cp in checkpoints:
        if cp["reward"] is not None:
            steps.append(cp["steps"])
            rewards.append(cp["reward"])

    if steps:
        plt.plot(steps, rewards, label=run)

plt.title(f"{behavior_to_plot} - Cumulative Reward Over Time")
plt.xlabel("Training Step")
plt.ylabel("Cumulative Reward")
plt.legend()
plt.grid(True)
plt.tight_layout()
plt.show()
