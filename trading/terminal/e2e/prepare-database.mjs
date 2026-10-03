// Gives the end-to-end tests an empty database of their own. Two services must never write to the
// same journal, so the tests never touch the development database.
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const composeFile = fileURLToPath(new URL("../../../deploy/docker-compose.yml", import.meta.url));
const psql = ["compose", "-f", composeFile, "exec", "-T", "postgres", "psql", "-U", "postgres", "-v", "ON_ERROR_STOP=1", "-c"];

for (const sql of ["drop database if exists trading_e2e with (force)", "create database trading_e2e owner trading"]) {
  execFileSync("docker", [...psql, sql], { stdio: "inherit" });
}
