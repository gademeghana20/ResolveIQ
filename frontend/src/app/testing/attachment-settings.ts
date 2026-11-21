import { AttachmentSettings } from '../features/incidents/incident.models';

/** What the server says about attaching files, as the tests like it: switched on, with the usual limits. */
export const ATTACHMENT_SETTINGS: AttachmentSettings = {
  enabled: true,
  maxFileBytes: 5 * 1024 * 1024,
  maxFilesPerIncident: 20,
  maxFilesPerComment: 5,
  allowedExtensions: ['.gif', '.jpeg', '.jpg', '.pdf', '.png', '.txt', '.webp'],
};
