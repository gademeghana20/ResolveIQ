import { IncidentPriority } from '../../incidents/incident.models';

/** The deadline rule for one priority: how many hours an incident of that priority has before it counts as late. */
export interface SlaPolicy {
  priority: IncidentPriority;
  resolutionTargetHours: number;
  /** False means incidents of this priority are not given a deadline at all. */
  isActive: boolean;
}

/** The limits the API enforces. */
export const MIN_TARGET_HOURS = 1;
export const MAX_TARGET_HOURS = 720; // 30 days
