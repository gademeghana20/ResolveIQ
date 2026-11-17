// The shapes of the dashboard data. They mirror the C# DTOs in ResolveIQ.Application/DTOs/Dashboard.

/** How many incidents are in each SLA state. */
export interface SlaCounts {
  notStarted: number;
  onTrack: number;
  atRisk: number;
  paused: number;
  breached: number;
  met: number;
  none: number;
}

/** The "what needs attention now" numbers. */
export interface DashboardSummary {
  totalIncidents: number;
  activeIncidents: number;
  assignedToMe: number;
  unassigned: number;
  oldestUnassignedWaitingMinutes: number | null;
  sla: SlaCounts;
  /** Active incidents by how long they have been open: Under1Day, 1To3Days, 3To7Days, Over7Days (always all four). */
  openAgeBuckets: CountItem[];
}

export interface CountItem {
  name: string;
  count: number;
}

export interface DailyCount {
  date: string; // "2026-11-17"
  count: number;
}

/** SLA compliance around one day: the share that met their SLA among those resolved in the 7 days up to it. */
export interface SlaComplianceDay {
  date: string;
  percent: number | null; // null when nothing with an SLA was resolved in that week
  resolved: number;
}

/** Average time to resolve for one priority. */
export interface PriorityResolution {
  priority: string; // Critical, High, Medium or Low
  averageHours: number | null; // null when none were resolved
  count: number;
}

export interface WorkloadItem {
  userId: string;
  email: string | null;
  activeIncidents: number;
  breachedIncidents: number;
}

/** The "how are we doing" numbers for a chosen number of days. */
export interface DashboardStatistics {
  days: number;
  from: string;
  to: string;
  byStatus: CountItem[];
  byPriority: CountItem[];
  byCategory: CountItem[];
  createdPerDay: DailyCount[];
  resolvedPerDay: DailyCount[];
  averageResolutionHours: number | null;
  slaCompliancePercent: number | null;
  slaComplianceTrend: SlaComplianceDay[];
  resolutionByPriority: PriorityResolution[];
  /** Only sent to Managers and Admins. */
  workload: WorkloadItem[] | null;
}

export const PERIOD_CHOICES = [7, 30, 90] as const;
