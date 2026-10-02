import { Command } from "commander";
import { clientFor, getGlobalOptions, handle } from "../context.js";
import { printResult } from "../output.js";

export function registerAvailabilityCommands(program: Command): void {
  const availability = program
    .command("availability")
    .description("Check resource availability");

  availability
    .command("check")
    .description(
      "Available slots for a location/date/party size (public endpoint)",
    )
    .requiredOption("--location <id>", "Venue/location id", Number)
    .requiredOption("--date <date>", "Date to check (ISO 8601)")
    .requiredOption("--party-size <n>", "Party size", Number)
    .action(
      handle(
        async (
          options: { location: number; date: string; partySize: number },
          command: Command,
        ) => {
          const { client } = clientFor(command);
          const globals = getGlobalOptions(command);
          const result = await client.get(
            `/api/venues/${options.location}/availability`,
            { query: { date: options.date, partySize: options.partySize } },
          );
          printResult(result, Boolean(globals.json));
        },
      ),
    );
}
