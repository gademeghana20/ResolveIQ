import { BadgeTone } from '../../shared/badge/badge';
import { IncidentPriority, IncidentStatus, SlaState } from './incident.models';

// How incident values are shown to people: friendly text and a badge colour.

export function statusLabel(status: IncidentStatus): string {
  return status === 'InProgress' ? 'In progress' : status === 'OnHold' ? 'On hold' : status;
}

export function statusTone(status: IncidentStatus): BadgeTone {
  switch (status) {
    case 'Open':
      return 'info';
    case 'InProgress':
      return 'warning';
    case 'Resolved':
      return 'success';
    default:
      return 'neutral'; // OnHold, Closed
  }
}

export function priorityLabel(priority: IncidentPriority | null): string {
  return priority ?? 'Not set'; // a manager sets it when assigning
}

export function priorityTone(priority: IncidentPriority | null): BadgeTone {
  switch (priority) {
    case 'Critical':
      return 'danger';
    case 'High':
      return 'warning';
    case 'Medium':
      return 'info';
    default:
      return 'neutral'; // Low, not set
  }
}

export function slaLabel(state: SlaState): string {
  switch (state) {
    case 'NotStarted':
      return 'Not started';
    case 'OnTrack':
      return 'On track';
    case 'AtRisk':
      return 'At risk';
    case 'None':
      return 'No SLA';
    default:
      return state; // Paused, Breached, Met
  }
}

export function slaTone(state: SlaState): BadgeTone {
  switch (state) {
    case 'OnTrack':
    case 'Met':
      return 'success';
    case 'AtRisk':
      return 'warning';
    case 'Breached':
      return 'danger';
    default:
      return 'neutral'; // NotStarted, Paused, None
  }
}
