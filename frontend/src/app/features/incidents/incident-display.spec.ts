import { priorityLabel, priorityTone, slaLabel, slaTone, statusLabel, statusTone } from './incident-display';

describe('incident display helpers', () => {
  it('writes statuses the way people read them', () => {
    expect(statusLabel('InProgress')).toBe('In progress');
    expect(statusLabel('OnHold')).toBe('On hold');
    expect(statusLabel('Open')).toBe('Open');
  });

  it('shows "Not set" for an incident nobody has prioritised yet', () => {
    expect(priorityLabel(null)).toBe('Not set');
    expect(priorityLabel('High')).toBe('High');
  });

  it('colours urgency: Critical is danger, High is warning', () => {
    expect(priorityTone('Critical')).toBe('danger');
    expect(priorityTone('High')).toBe('warning');
    expect(priorityTone(null)).toBe('neutral');
  });

  it('colours a breached SLA as danger and one at risk as a warning', () => {
    expect(slaTone('Breached')).toBe('danger');
    expect(slaTone('AtRisk')).toBe('warning');
    expect(slaTone('OnTrack')).toBe('success');
    expect(slaTone('Met')).toBe('success');
    expect(slaTone('Paused')).toBe('neutral');
  });

  it('writes SLA states as readable text', () => {
    expect(slaLabel('NotStarted')).toBe('Not started');
    expect(slaLabel('AtRisk')).toBe('At risk');
    expect(slaLabel('None')).toBe('No SLA');
    expect(slaLabel('Breached')).toBe('Breached');
  });

  it('colours statuses', () => {
    expect(statusTone('Open')).toBe('info');
    expect(statusTone('Resolved')).toBe('success');
    expect(statusTone('Closed')).toBe('neutral');
  });
});
