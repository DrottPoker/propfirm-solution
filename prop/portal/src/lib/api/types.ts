import type { components } from "./schema";

type Schemas = components["schemas"];

export type Account = Schemas["AccountResponse"];
export type AccountDetails = Schemas["AccountDetailsResponse"];
export type LiveFigures = Schemas["LiveFigures"];
export type FloorFigure = Schemas["FloorFigure"];
export type BreachEvidence = Schemas["BreachEvidence"];
export type Branding = Schemas["Branding"];
export type ChallengeDefinition = Schemas["ChallengeDefinition"];
export type ChallengeStatus = Schemas["ChallengeStatus"];
export type FailureReason = Schemas["FailureReason"];
export type Me = Schemas["PortalMeResponse"];
export type Step = Schemas["StepResponse"];
