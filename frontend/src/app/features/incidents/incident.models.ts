// The shapes of the incident data the API sends. They mirror the C# DTOs in
// ResolveIQ.Application/DTOs/Incidents. The API writes enums as text, so these are text unions.

export type IncidentStatus = 'Open' | 'InProgress' | 'OnHold' | 'Resolved' | 'Closed';
export type IncidentPriority = 'Low' | 'Medium' | 'High' | 'Critical';
export type SlaState = 'None' | 'NotStarted' | 'OnTrack' | 'AtRisk' | 'Paused' | 'Breached' | 'Met';
export type IncidentSortField = 'CreatedAt' | 'Priority' | 'Status' | 'IncidentNumber' | 'Title';
export type SortDirection = 'Asc' | 'Desc';

export const INCIDENT_STATUSES: IncidentStatus[] = ['Open', 'InProgress', 'OnHold', 'Resolved', 'Closed'];
export const INCIDENT_PRIORITIES: IncidentPriority[] = ['Critical', 'High', 'Medium', 'Low'];
export const SLA_STATES: SlaState[] = ['NotStarted', 'OnTrack', 'AtRisk', 'Paused', 'Breached', 'Met', 'None'];

/** One row of the incident list. */
export interface IncidentSummary {
  id: string;
  incidentNumber: string;
  title: string;
  priority: IncidentPriority | null;
  status: IncidentStatus;
  categoryName: string | null;
  reportedByUserId: string;
  assignedToUserId: string | null;
  reportedByEmail: string | null;
  assignedToEmail: string | null;
  createdAtUtc: string; // ISO date text, e.g. "2026-11-16T18:00:00Z"
  slaState: SlaState;
  slaDueAtUtc: string | null;
}

/** One page of results, with what we need to draw the paginator. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** The search, filter, sort and paging options the list sends to the API. */
export interface IncidentQuery {
  search?: string;
  status?: IncidentStatus;
  priority?: IncidentPriority;
  categoryId?: string;
  slaState?: SlaState;
  assignedToMe?: boolean;
  reportedByMe?: boolean;
  sortBy: IncidentSortField;
  sortDirection: SortDirection;
  page: number;
  pageSize: number;
}

export interface Category {
  id: string;
  name: string;
  description: string | null;
  isActive: boolean;
}
