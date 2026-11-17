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
}

export interface CountItem {
  name: string;
  count: number;
}

export interface DailyCount {
  date: string; // "2026-11-17"
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
  /** Only sent to Managers and Admins. */
  workload: WorkloadItem[] | null;
}

export const PERIOD_CHOICES = [7, 30, 90] as const;
