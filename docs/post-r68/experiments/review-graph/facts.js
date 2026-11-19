// Public-source fixture. Revisions identify the pinned research inputs.
const billSource = {
    name: "GovInfo",
    href: "https://www.govinfo.gov/content/pkg/BILLS-119s146es/html/BILLS-119s146es.htm",
    revision: "d9426c01521a9087e085bbab2a4b472dd5a2f347514116612635151c8b99f0b4",
};
const voteSource = {
    name: "House Clerk",
    href: "https://clerk.house.gov/Votes/2025104",
    revision: "7e3474ac0c18abefed41bdae758f7ef99d59cfde98d26a52ad169023cdf4da27",
};
const sourceNodes = {
    provision: {
        kind: "Provision",
        title: "Removal requirement",
        meta: "Section 3(a)(3)",
        source: billSource,
        excerpt: "as soon as possible, but not later than 48 hours after receiving such request",
        showQualification: true,
        detail: "The deadline starts when a covered platform receives a valid removal request. The provision also requires reasonable efforts to find and remove known identical copies.",
    },
    bill: {
        kind: "Bill edition",
        title: "S. 146",
        meta: "Senate-passed text",
        source: billSource,
        detail: "The Senate passed this edition of the TAKE IT DOWN Act on February 13, 2025. Section 3 sets out the notice and removal process.",
    },
    vote: {
        kind: "Roll call",
        title: "House roll 104",
        meta: "April 28, 2025",
        source: voteSource,
        detail: "On Motion to Suspend the Rules and Pass. The House passed S. 146 with 409 yeas and 2 nays. The roll call covers the whole bill.",
    },
    cast: {
        kind: "Member cast",
        title: "Dean voted Yea",
        meta: "Pennsylvania · House",
        source: voteSource,
        detail: "The individual entry for Dean (PA), member ID D000631, records a Yea on House roll 104.",
    },
};
const sourceEdges = [
    {
        id: "part",
        from: "provision",
        to: "bill",
        verb: "part of",
        detail: "Section 3(a)(3) belongs to this Senate-passed edition.",
    },
    {
        id: "measure",
        from: "vote",
        to: "bill",
        verb: "voted on",
        detail: "The Clerk records S. 146 as the measure. The roll call decided passage of the bill as a whole.",
    },
    {
        id: "recorded",
        from: "cast",
        to: "vote",
        verb: "recorded in",
        detail: "Dean's individual vote appears in the member entries of this roll call.",
    },
];
