// Gives the end-to-end tests empty databases of their own, one per product, so they never touch the
// development databases.
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const composeFile = fileURLToPath(new URL("../../../deploy/docker-compose.yml", import.meta.url));
const psql = ["compose", "-f", composeFile, "exec", "-T", "postgres", "psql", "-U", "postgres", "-v", "ON_ERROR_STOP=1", "-c"];

for (const [database, owner] of [
  ["trading_portal_e2e", "trading"],
  ["prop_e2e", "prop"],
]) {
  for (const sql of [`drop database if exists ${database} with (force)`, `create database ${database} owner ${owner}`]) {
    execFileSync("docker", [...psql, sql], { stdio: "inherit" });
  }
}
