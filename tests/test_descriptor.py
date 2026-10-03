"""Independently parse HID item widths from the C descriptor."""
from collections import defaultdict
from pathlib import Path
import re
import unittest


def descriptor_bytes():
    source = (Path(__file__).resolve().parents[1] / "driver/report_descriptor.h").read_text()
    source = re.sub(r"/\*.*?\*/", "", source, flags=re.S)
    finger = source.split("#define PTP_FINGER", 1)[1].split("static const", 1)[0]
    body = source.split("PtpDescriptor[] = {", 1)[1].split("};", 1)[0].replace("PTP_FINGER", finger)
    return bytes(int(value, 16) for value in re.findall(r"0x([0-9a-fA-F]+)", body))


def parse_items(data):
    index = 0
    while index < len(data):
        prefix = data[index]
        size = (0, 1, 2, 4)[prefix & 3]
        if prefix == 0xFE or index + 1 + size > len(data):
            raise ValueError("Invalid HID item")
        value = int.from_bytes(data[index + 1:index + 1 + size], "little")
        yield (prefix >> 2) & 3, prefix >> 4, value
        index += 1 + size


class DescriptorTests(unittest.TestCase):
    def test_host_button_and_switch_collection(self):
        data = descriptor_bytes()
        self.assertIn(bytes.fromhex("05 09 09 01 25 01 75 01 95 01 81 02"), data)
        self.assertIn(bytes.fromhex("85 05 09 22 a1 00 09 57 09 58"), data)

    def test_report_layout_and_collections(self):
        sizes = defaultdict(int)
        page = usage = report_id = width = count = depth = 0
        top_level = []
        contacts = 0
        for kind, tag, value in parse_items(descriptor_bytes()):
            if kind == 1:
                if tag == 0: page = value
                elif tag == 7: width = value
                elif tag == 8: report_id = value
                elif tag == 9: count = value
            elif kind == 2 and tag == 0:
                usage = value
            elif kind == 0:
                if tag == 10:
                    if depth == 0: top_level.append((page, usage))
                    if page == 13 and usage == 34 and report_id == 1: contacts += 1
                    depth += 1
                elif tag == 12:
                    depth -= 1
                    self.assertGreaterEqual(depth, 0)
                elif tag in (8, 9, 11):
                    sizes[(tag, report_id)] += width * count
                usage = 0
        self.assertEqual(depth, 0)
        self.assertEqual(top_level, [(13, 5), (13, 14)])
        self.assertEqual(contacts, 5)
        self.assertEqual(sizes[(8, 1)], 34 * 8)
        self.assertEqual(sizes[(11, 2)], 2 * 8)
        self.assertEqual(sizes[(11, 3)], 256 * 8)
        self.assertEqual(sizes[(11, 4)], 8)
        self.assertEqual(sizes[(11, 5)], 8)


if __name__ == "__main__":
    unittest.main()
