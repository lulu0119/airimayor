import type { MayorPlan } from "./chat-types";
import { useChatText } from "./locale";
import styles from "./chat.module.scss";

interface PlanStripProps {
  plan: MayorPlan | null;
}

export const PlanStrip = ({ plan }: PlanStripProps) => {
  const text = useChatText();
  if (plan == null) {
    return (
      <div className={styles.planStrip}>
        <span className={styles.planEmpty}>{text("Plan.None", "No active plan")}</span>
      </div>
    );
  }
  return (
    <div className={styles.planStrip}>
      <span className={styles.planGoal}>{plan.goal}</span>
      {plan.success ? <span className={styles.planSuccess}>{plan.success}</span> : null}
    </div>
  );
};
