#!/usr/bin/env node
import { Command } from "commander";
import { registerAuthCommands } from "./commands/auth.js";
import { registerBookingsCommands } from "./commands/bookings.js";
import { registerAvailabilityCommands } from "./commands/availability.js";
import { registerLocationsCommands } from "./commands/locations.js";
import { registerResourcesCommands } from "./commands/resources.js";
import { registerSectionsCommands } from "./commands/sections.js";
import { registerBrandCommands } from "./commands/brand.js";
import { registerUsersCommands } from "./commands/users.js";
import { registerAuditCommands } from "./commands/audit.js";
import { registerEmailCommands } from "./commands/email.js";
import { registerStatusCommand } from "./commands/status.js";
import { getCliVersion } from "./version.js";

const version = getCliVersion();

const program = new Command();

program
  .name("resourceflow")
  .description("Command-line client for the ResourceFlow admin API")
  .version(version)
  .option("--profile <name>", 'Named profile to use (default: "default")')
  .option("--json", "Output raw JSON instead of a resource")
  .option("--yes", "Skip confirmation prompts on destructive commands");

registerStatusCommand(program);
registerAuthCommands(program);
registerBookingsCommands(program);
registerAvailabilityCommands(program);
registerLocationsCommands(program);
registerResourcesCommands(program);
registerSectionsCommands(program);
registerBrandCommands(program);
registerUsersCommands(program);
registerAuditCommands(program);
registerEmailCommands(program);

await program.parseAsync(process.argv);
