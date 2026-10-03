import type { components } from "./schema";

type Schemas = components["schemas"];

export type Account = Schemas["AccountResponse"];
export type AccountDetails = Schemas["AccountDetailsResponse"];
export type LiveFigures = Schemas["LiveFigures"];
export type FloorFigure = Schemas["FloorFigure"];
export type BreachEvidence = Schemas["BreachEvidence"];
export type Branding = Schemas["BrandingResponse"];
export type ChallengeDefinition = Schemas["ChallengeDefinition"];
export type ChallengeStatus = Schemas["ChallengeStatus"];
export type FailureReason = Schemas["FailureReason"];
export type Me = Schemas["PortalMeResponse"];
export type Step = Schemas["StepResponse"];
export type Payout = Schemas["PayoutResponse"];
export type PayoutQuote = Schemas["PayoutQuoteResponse"];
export type PayoutStatus = Schemas["PayoutStatus"];
export type FirmStatus = Schemas["FirmStatus"];
export type FirmSettings = Schemas["FirmSettingsResponse"];
export type Platform = Schemas["PlatformResponse"];
export type Admins = Schemas["AdminsResponse"];
export type ChallengeTemplate = Schemas["ChallengeTemplateResponse"];
export type StageRules = Schemas["StageRules"];
