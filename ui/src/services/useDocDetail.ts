import { fetchDocument } from './api';
import { createLazyResource } from './lazyHydration';
import type { DocumentDetail } from '../types';

/** Lazily hydrates a doc's full drill-down (GET /docs/{doc_id}) — one
 *  fetch per docId, ever, shared by every PostCard on the page. */
export const useDocDetail = createLazyResource<DocumentDetail>(fetchDocument);
