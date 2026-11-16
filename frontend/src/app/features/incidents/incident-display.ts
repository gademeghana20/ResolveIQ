import { BadgeTone } from '../../shared/badge/badge';
import { HistoryEntry, IncidentPriority, IncidentStatus, SlaState } from './incident.models';

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

/** Turns one history row into a sentence a person can read. */
export function describeChange(entry: HistoryEntry): string {
  const from = entry.oldValue;
  const to = entry.newValue;

  switch (entry.fieldChanged) {
    case 'Status':
      return `Status changed from ${statusText(from)} to ${statusText(to)}`;
    case 'Priority':
      return `Priority changed from ${from ?? 'Not set'} to ${to ?? 'Not set'}`;
    case 'AssignedTo':
      return from ? `Reassigned from ${from} to ${to}` : `Assigned to ${to}`;
    case 'SlaClock':
      return to === 'Started' ? 'SLA clock started' : `SLA clock restarted (it was ${slaLabelText(from)})`;
    case 'OnHoldReason':
      return `Put on hold: ${to}`;
    case 'Title':
      return `Title changed from "${from}" to "${to}"`;
    case 'Description':
      return 'Description edited';
    default:
      return `${entry.fieldChanged} changed`;
  }
}

function statusText(value: string | null): string {
  return value ? statusLabel(value as IncidentStatus) : 'nothing';
}

function slaLabelText(value: string | null): string {
  return value ? slaLabel(value as SlaState).toLowerCase() : 'not running';
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
