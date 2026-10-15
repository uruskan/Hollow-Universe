# Snapshot bandwidth repair

The held-action replication adds an item and server start tick to character state. The inherited snapshot budget
used the maximum varint width even for zero frame/action values. This reduced the number of newly visible players
sent per packet and failed the unchanged requirement to deliver 300 entities within nineteen 600-byte packets.

Generated state serializers now expose their exact encoded size and count actual varint widths in deltas. The
snapshot sender measures its generated header instead of maintaining a second list of fields by hand. That also
accounts for the new cruise axes and fractional fuel reserve in a pilot's state. The existing priority, baseline,
acknowledgement and packet-size limits are unchanged; the fix does not increase the bandwidth allowance.

`SnapshotRepairTests` exercises every uint varint boundary, ulong boundaries, full and delta size agreement, and
crowded 600-byte packets containing a pilot, ships and action-bearing characters with large IDs. Existing loss,
priority and every-message round-trip checks remain intact. The coordinated focused run passed **68/68** checks,
including both new snapshot cases and all selected network suites (`.artifacts/repair-focused5.log`).

Protocol version 29 additionally identifies the held-action semantics. Layout compatibility is checked through the
generated schema hash, including fuel, attachment and rename fields. Clients and servers must use the same build.
