import { describeChange, priorityLabel, priorityTone, slaLabel, slaTone, statusLabel, statusTone } from './incident-display';

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

  describe('describeChange', () => {
    const row = (fieldChanged: string, oldValue: string | null, newValue: string | null) => ({
      fieldChanged,
      oldValue,
      newValue,
      changedBy: 'someone@example.com',
      changedAtUtc: '2026-11-16T18:00:00Z',
    });

    it('describes a status change with friendly status names', () => {
      expect(describeChange(row('Status', 'InProgress', 'OnHold'))).toBe('Status changed from In progress to On hold');
    });

    it('describes setting and changing a priority', () => {
      expect(describeChange(row('Priority', null, 'High'))).toBe('Priority changed from Not set to High');
      expect(describeChange(row('Priority', 'High', 'Critical'))).toBe('Priority changed from High to Critical');
    });

    it('distinguishes a first assignment from a reassignment', () => {
      expect(describeChange(row('AssignedTo', null, 'a@example.com'))).toBe('Assigned to a@example.com');
      expect(describeChange(row('AssignedTo', 'a@example.com', 'b@example.com'))).toBe(
        'Reassigned from a@example.com to b@example.com',
      );
    });

    it('describes the SLA clock starting and restarting', () => {
      expect(describeChange(row('SlaClock', null, 'Started'))).toBe('SLA clock started');
      expect(describeChange(row('SlaClock', 'AtRisk', 'Restarted'))).toBe('SLA clock restarted (it was at risk)');
    });

    it('shows the reason for putting an incident on hold', () => {
      expect(describeChange(row('OnHoldReason', null, 'Waiting for parts'))).toBe('Put on hold: Waiting for parts');
    });

    it('does not dump a whole edited description into the timeline', () => {
      expect(describeChange(row('Description', 'long old text', 'long new text'))).toBe('Description edited');
    });

    it('copes with a field it does not know about', () => {
      expect(describeChange(row('Something', 'a', 'b'))).toBe('Something changed');
    });
  });

  it('colours statuses', () => {
    expect(statusTone('Open')).toBe('info');
    expect(statusTone('Resolved')).toBe('success');
    expect(statusTone('Closed')).toBe('neutral');
  });

  describe('describeChange for files', () => {
    const row = (fieldChanged: string, oldValue: string | null, newValue: string | null) => ({
      fieldChanged,
      oldValue,
      newValue,
      changedBy: 'dana@example.com',
      changedAtUtc: '2026-11-21T10:00:00Z',
    });

    it('says which file was attached', () => {
      expect(describeChange(row('AttachmentAdded', null, 'shot.png'))).toBe('Attached the file "shot.png"');
    });

    it('says which file was removed', () => {
      expect(describeChange(row('AttachmentRemoved', 'shot.png', null))).toBe('Removed the file "shot.png"');
    });
  });

});
