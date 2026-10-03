import type { components } from "./schema";

type Schemas = components["schemas"];

export type AccountSnapshot = Schemas["AccountSnapshot"];
export type PositionSnapshot = Schemas["PositionSnapshot"];
export type OrderSnapshot = Schemas["OrderSnapshot"];
export type FloorSnapshot = Schemas["FloorSnapshot"];
export type SymbolPrice = Schemas["SymbolPrice"];
export type InstrumentInfo = Schemas["InstrumentInfo"];
export type PointValue = Schemas["PointValue"];
export type Candle = Schemas["Candle"];
export type EventEnvelope = Schemas["EventEnvelope"];
export type EngineEvent = Schemas["EngineEvent"];
export type PlaceOrderRequest = Schemas["PlaceOrderRequest"];
export type CommandResponse = Schemas["CommandResponse"];
export type Side = Schemas["Side"];
export type OrderType = Schemas["OrderType"];
export type Timeframe = Schemas["Timeframe"];
export type ServerInfo = Schemas["ServerInfo"];
export type Me = Schemas["MeResponse"];
