import struct
import unittest

from precision_bridge.protocol import Contact, StreamDecoder
from precision_bridge.reports import ReportEncoder, REPORT_SIZE


def record(active=1, x=1580, pressure=33):
    return ("Data=5," + ",".join(map(str, [active, x, 895, 3, pressure] + [0] * 20))).encode()


class ProtocolTests(unittest.TestCase):
    def test_every_single_split(self):
        payload = record() + record(0) + record(1, 1800)
        for offset in range(len(payload) + 1):
            decoder = StreamDecoder()
            frames = decoder.feed(payload[:offset]) + decoder.feed(payload[offset:])
            self.assertEqual([f[0].active for f in frames], [True, False, True], offset)
            self.assertEqual([f[0].x for f in frames], [1580, 1580, 1800], offset)

    def test_byte_by_byte(self):
        decoder = StreamDecoder()
        frames = []
        for byte in record() + record(0):
            frames.extend(decoder.feed(bytes([byte])))
        self.assertEqual(len(frames), 2)

    def test_final_pressure_not_needed(self):
        decoder = StreamDecoder()
        frame = record().rsplit(b",", 1)[0] + b","
        self.assertEqual(len(decoder.feed(frame)), 1)
        self.assertEqual(decoder.feed(b"123"), [])

    def test_recovery(self):
        decoder = StreamDecoder()
        self.assertEqual(len(decoder.feed(b"garbageData=broken" + record())), 1)
        self.assertEqual(decoder.feed(b"x" * 3000), [])
        self.assertEqual(len(decoder.feed(record())), 1)

    def test_multiple_contacts(self):
        values = [1, 100, 200, 3, 20, 1, 300, 400, 4, 30] + [0] * 15
        frames = StreamDecoder().feed(("Data=5," + ",".join(map(str, values))).encode())
        self.assertEqual(sum(c.active for c in frames[0]), 2)

    def test_reject_bad_active(self):
        self.assertEqual(StreamDecoder().feed(record(7)), [])


class ReportTests(unittest.TestCase):
    def test_scale_and_release(self):
        encoder = ReportEncoder((3420, 2052))
        first = encoder.encode([Contact(2, True, 3420, 2052, 3)], 65535)
        self.assertEqual(len(first), REPORT_SIZE)
        self.assertEqual(struct.unpack_from("<BBHH", first, 1), (3, 2, 4095, 4095))
        self.assertEqual(struct.unpack_from("<HBB", first, 31), (65535, 1, 0))
        up = encoder.release(65536)
        self.assertEqual(struct.unpack_from("<BBHH", up, 1), (1, 2, 4095, 4095))
        self.assertEqual(struct.unpack_from("<HBB", up, 31), (0, 1, 0))
        self.assertEqual(encoder.release(65537)[33], 0)

    def test_all_five_lift(self):
        encoder = ReportEncoder((3420, 2052))
        encoder.encode([Contact(i, True, 100 * i, 200 * i, 3) for i in range(5)], 10)
        report = encoder.release(11)
        self.assertEqual(report[33], 5)
        self.assertEqual([report[1 + i * 6] for i in range(5)], [1] * 5)

    def test_partial_lift(self):
        encoder = ReportEncoder((3420, 2052))
        encoder.encode([Contact(i, True, 100, 100, 3) for i in range(2)], 1)
        report = encoder.encode([Contact(1, True, 200, 200, 3)], 2)
        self.assertEqual(report[33], 2)
        self.assertEqual(report[1:3], b"\x03\x01")
        self.assertEqual(report[7:9], b"\x01\x00")

    def test_clamp_and_inversion(self):
        report = ReportEncoder((100, 100), True).encode([Contact(0, True, 200, 0, 3)], 1)
        self.assertEqual(struct.unpack_from("<HH", report, 3), (4095, 4095))


if __name__ == "__main__":
    unittest.main()
