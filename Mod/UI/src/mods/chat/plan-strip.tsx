import { Icon } from "@iconify/react";
import checkCircleBoldDuotone from "@iconify-icons/solar/check-circle-bold-duotone";
import clockCircleBoldDuotone from "@iconify-icons/solar/clock-circle-bold-duotone";
import playCircleBoldDuotone from "@iconify-icons/solar/play-circle-bold-duotone";
import type { PlanEntry, PlanStatus } from "./chat-types";
import { useChatText } from "./locale";
import styles from "./chat.module.scss";

const statusIcon = {
  pending: clockCircleBoldDuotone,
  in_progress: playCircleBoldDuotone,
  completed: checkCircleBoldDuotone,
} as const;

const statusClass: Record<PlanStatus, string> = {
  pending: styles.planPending,
  in_progress: styles.planActive,
  completed: styles.planDone,
};

export const PlanStrip = ({ plan }: { plan: PlanEntry[] | null }) => {
  const text = useChatText();
  if (plan == null || plan.length === 0) {
    return (
      <div className={styles.planStrip}>
        <span className={styles.planEmpty}>{text("Plan.None", "No active plan")}</span>
      </div>
    );
  }
  const statusLabel: Record<PlanStatus, string> = {
    pending: text("Plan.Pending", "Pending"),
    in_progress: text("Plan.InProgress", "In progress"),
    completed: text("Plan.Completed", "Completed"),
  };
  return (
    <div className={styles.planStrip}>
      {plan.map((entry, index) => (
        <div className={styles.planEntry} key={`${index}:${entry.content}`}>
          <span className={`${styles.planStatus} ${statusClass[entry.status]}`} title={statusLabel[entry.status]}>
            <Icon icon={statusIcon[entry.status]} />
          </span>
          <div className={styles.planContent}>{entry.content}</div>
        </div>
      ))}
    </div>
  );
};
