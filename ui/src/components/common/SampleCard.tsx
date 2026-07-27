// --------------------------------------------------------------------------- //
//  SampleCard — retired name kept as a thin re-export of PostCard/           //
//  PostCardList (see ./PostCard.tsx) so existing page imports keep          //
//  compiling. The Bloomberg-dense card visuals live in PostCard now; this    //
//  file carries no logic of its own.                                        //
// --------------------------------------------------------------------------- //

export { PostCard as default, PostCard as SampleCard, PostCardList as SampleCardList } from './PostCard';
