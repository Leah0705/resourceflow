import { get } from "./client";

export interface TimeSlotDto {
  time: string;
  isAvailable: boolean;
  availableResourceIds: number[];
  /** Combinable-resource group ids bookable for this slot — parallel to availableResourceIds. */
  availableGroupIds?: number[];
  category: "AM" | "PM";
}

export interface AvailabilityResponseDto {
  venueId: number;
  date: string;
  slots: TimeSlotDto[];
}

export async function fetchAvailability(
  venueId: number,
  date: string,
  partySize: number
): Promise<AvailabilityResponseDto | null> {
  try {
    const res = await get(`/venues/${venueId}/availability?date=${date}&partySize=${partySize}`);
    if (!res.ok) return null;
    return await res.json();
  } catch (err) {
    console.error("fetchAvailability error:", err);
    return null;
  }
}
