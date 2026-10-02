import { Command } from "commander";
import { clientFor, getGlobalOptions, handle } from "../context.js";
import { printResult } from "../output.js";
import { confirmOrExit } from "../confirm.js";

interface Resource {
  id: number;
  name: string | null;
  capacity: number;
}
interface Section {
  id: number;
  name: string;
  resources: Resource[];
}

const LIST_COLUMNS = [
  "sectionId",
  "sectionName",
  "resourceId",
  "resourceName",
  "capacity",
];

/**
 * Resources and sections share one CRUD surface server-side (`VenuesController`'s
 * `{id}/sections/{sectionId}/resources/{resourceId}` routes) — a resource cannot be created, updated, or
 * deleted without naming the section it lives in. Rather than build a parallel `sections` command
 * group, `resources list` prints the section id/name alongside each resource so `--section` has
 * something to point at; full section CRUD (rename, reorder, delete) is left to the admin UI.
 */
export function registerResourcesCommands(program: Command): void {
  const resources = program
    .command("resources")
    .description("Manage resources (grouped by section)");

  resources
    .command("list")
    .description("List a location's sections and resources")
    .requiredOption("--location <id>", "Venue/location id", Number)
    .action(
      handle(async (options: { location: number }, command: Command) => {
        const { client } = clientFor(command);
        const globals = getGlobalOptions(command);
        const sections = await client.get<Section[]>(
          `/api/admin/venues/${options.location}/resources`,
        );
        const rows = sections.flatMap<Record<string, unknown>>((section) =>
          section.resources.length > 0
            ? section.resources.map((resource) => ({
                sectionId: section.id,
                sectionName: section.name,
                resourceId: resource.id,
                resourceName: resource.name,
                capacity: resource.capacity,
              }))
            : [
                {
                  sectionId: section.id,
                  sectionName: section.name,
                  resourceId: "",
                  resourceName: "(no resources)",
                  capacity: "",
                },
              ],
        );
        printResult(
          globals.json ? sections : rows,
          Boolean(globals.json),
          LIST_COLUMNS,
        );
      }),
    );

  resources
    .command("create")
    .description("Add a resource to a section")
    .requiredOption("--location <id>", "Venue/location id", Number)
    .requiredOption("--section <id>", "Section id (see `resources list`)", Number)
    .requiredOption("--capacity <n>", "Capacity", Number)
    .option("--name <name>", "Resource name")
    .action(
      handle(
        async (
          options: {
            location: number;
            section: number;
            capacity: number;
            name?: string;
          },
          command: Command,
        ) => {
          const { client } = clientFor(command);
          const globals = getGlobalOptions(command);
          const result = await client.post(
            `/api/venues/${options.location}/sections/${options.section}/resources`,
            { body: { name: options.name, capacity: options.capacity } },
          );
          printResult(result, Boolean(globals.json));
        },
      ),
    );

  resources
    .command("update <resourceId>")
    .description("Rename a resource or change its capacity")
    .requiredOption("--location <id>", "Venue/location id", Number)
    .requiredOption("--section <id>", "Section id (see `resources list`)", Number)
    .requiredOption("--capacity <n>", "Capacity", Number)
    .option("--name <name>", "Resource name")
    .action(
      handle(
        async (
          resourceId: string,
          options: {
            location: number;
            section: number;
            capacity: number;
            name?: string;
          },
          command: Command,
        ) => {
          const { client } = clientFor(command);
          const globals = getGlobalOptions(command);
          const result = await client.put(
            `/api/venues/${options.location}/sections/${options.section}/resources/${encodeURIComponent(resourceId)}`,
            { body: { name: options.name, capacity: options.capacity } },
          );
          printResult(result, Boolean(globals.json));
        },
      ),
    );

  resources
    .command("delete <resourceId>")
    .description(
      "Remove a resource — bookings referencing it keep their booking, losing only the resource link",
    )
    .requiredOption("--location <id>", "Venue/location id", Number)
    .requiredOption("--section <id>", "Section id (see `resources list`)", Number)
    .option("--yes", "Skip the confirmation prompt")
    .action(
      handle(
        async (
          resourceId: string,
          options: { location: number; section: number; yes?: boolean },
          command: Command,
        ) => {
          const { client } = clientFor(command);
          const globals = getGlobalOptions(command);

          let message = `This will remove resource ${resourceId}.`;
          try {
            const impact = await client.get<{ bookings: number }>(
              `/api/venues/${options.location}/sections/${options.section}/resources/${resourceId}/impact`,
            );
            if (impact.bookings > 0) {
              message += ` ${impact.bookings} upcoming booking(s) will lose their resource reference.`;
            }
          } catch {
            // Best-effort preview only.
          }

          await confirmOrExit(message, Boolean(options.yes ?? globals.yes));
          await client.delete(
            `/api/venues/${options.location}/sections/${options.section}/resources/${encodeURIComponent(resourceId)}`,
          );
          console.log(`Resource ${resourceId} removed.`);
        },
      ),
    );
}
