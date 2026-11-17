/** A kind of incident (Hardware, Network…), as the API describes it. */
export interface Category {
  id: string;
  name: string;
  description: string | null;
  /** False for a hidden category: it is not offered for new incidents, but old incidents keep it. */
  isActive: boolean;
}
