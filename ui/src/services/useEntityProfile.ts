import { fetchEntityProfile } from './api';
import { createLazyResource } from './lazyHydration';
import type { EntityProfileResponse } from '../types';

/** Lazily hydrates an entity's all-time profile (GET /entity-profile/{id})
 *  — one fetch per entityId, ever, shared by every EntityProfileCard. */
export const useEntityProfile = createLazyResource<EntityProfileResponse>(fetchEntityProfile);
