"""Hold-one/tap-another recognizer, independent of the Windows input APIs."""
import math


class ChordRightClick:
    def __init__(self, logical_size, size_mm=(107.2, 64.3)):
        self.scale = (size_mm[0] / logical_size[0], size_mm[1] / logical_size[1])
        self.down_since = {}
        self.previous = {}
        self.candidate = None
        self.attempt = None
        self.sequence = 0

    def snapshot(self, now, config):
        hold = (now - next(iter(self.down_since.values()))) * 1000 if len(self.previous) == 1 else 0
        return {'liveHoldMs': max(0, hold), 'ready': hold >= config['rightClickHoldMs'],
                'enabled': config['chordRightClickEnabled'], 'attempt': self.attempt}

    def observe_attempt(self, active, now, config, buttons_down):
        new = set(active) - set(self.previous)
        if len(active) == 2 and len(new) and not (self.attempt and not self.attempt['finished']):
            anchor = next(iter(self.previous)) if len(self.previous) == 1 else next(iter(active))
            if anchor not in active:
                anchor = next(iter(active))
            tapping = next(slot for slot in active if slot != anchor)
            hold = (now - self.down_since.get(anchor, now)) * 1000
            self.sequence += 1
            self.attempt = {'id': self.sequence, 'anchor': anchor, 'tapping': tapping, 'start': now,
                'holdMs': max(0, hold), 'tapMs': 0, 'motionMm': 0, 'finished': False,
                'accepted': False, 'reason': 'Tap in progress',
                'minimumHoldMs': config['rightClickHoldMs'], 'maximumTapMs': config['rightClickTapMaxMs'],
                'slopMm': config['rightClickSlopMm'],
                'origins': {slot: (c.x, c.y) for slot, c in active.items()}}
            if not config['chordRightClickEnabled']:
                self.attempt['reason'] = 'Feature disabled'
            elif len(self.previous) != 1 or hold < config['rightClickHoldMs']:
                self.attempt['reason'] = 'First finger was not held long enough'
        attempt = self.attempt
        if not attempt or attempt['finished']:
            return
        attempt['tapMs'] = max(0, (now - attempt['start']) * 1000)
        for slot, origin in attempt['origins'].items():
            if slot in active:
                attempt['motionMm'] = max(attempt['motionMm'], self.distance(active[slot], origin))
        if buttons_down:
            attempt['reason'] = 'Physical mouse button held'
        elif attempt['anchor'] not in active:
            attempt['reason'] = 'First finger lifted'
        elif len(active) > 2:
            attempt['reason'] = 'Extra finger detected'
        elif attempt['motionMm'] > attempt['slopMm']:
            attempt['reason'] = 'Too much movement'
        elif attempt['tapMs'] > attempt['maximumTapMs']:
            attempt['reason'] = 'Second finger held too long'
        if attempt['tapping'] not in active or attempt['anchor'] not in active or len(active) > 2 or buttons_down:
            attempt['finished'] = True

    def distance(self, contact, origin):
        return math.hypot((contact.x - origin[0]) * self.scale[0],
                          (contact.y - origin[1]) * self.scale[1])

    def update(self, contacts, now, config, buttons_down=False):
        active = {c.slot: c for c in contacts if c.active}
        self.observe_attempt(active, now, config, buttons_down)
        click = False
        if buttons_down:
            self.candidate = None
        if self.candidate:
            candidate = self.candidate
            anchor, tapping = candidate['anchor'], candidate['tapping']
            elapsed = (now - candidate['time']) * 1000
            if tapping not in active:
                click = (candidate['valid'] and set(active) == {anchor} and
                         0 < elapsed <= config['rightClickTapMaxMs'])
                self.candidate = None
            elif set(active) != {anchor, tapping}:
                self.candidate = None
            elif elapsed > config['rightClickTapMaxMs'] or any(
                self.distance(active[slot], candidate['origins'][slot]) > config['rightClickSlopMm']
                for slot in (anchor, tapping)):
                candidate['valid'] = False
        new = set(active) - set(self.previous)
        if not buttons_down and config['chordRightClickEnabled'] and self.candidate is None and len(self.previous) == 1 and len(active) == 2 and len(new) == 1:
            anchor = next(iter(self.previous))
            if anchor in active and (now - self.down_since[anchor]) * 1000 >= config['rightClickHoldMs']:
                tapping = next(iter(new))
                self.candidate = {'anchor': anchor, 'tapping': tapping, 'time': now, 'valid': True,
                    'origins': {slot: (active[slot].x, active[slot].y) for slot in active}}
        self.down_since = {slot: self.down_since.get(slot, now) for slot in active}
        self.previous = active
        if not config['chordRightClickEnabled']:
            click = False
            self.candidate = None
        if click and self.attempt:
            self.attempt.update(accepted=True, finished=True, reason='Right-click accepted')
        elif self.attempt and self.attempt['finished'] and self.attempt['reason'] == 'Tap in progress':
            self.attempt['reason'] = 'Tap rejected'
        return click


class GestureMotionTracker:
    """Track gesture movement without withholding stationary Windows contacts.

    Windows must see touchdown immediately to cancel inertia. Motion tracking
    only decides when participants earn the bottom-zone scrolling exemption.
    Ordinary right-click suppression belongs to the ASUS/Windows tap settings.
    """
    def __init__(self, logical_size):
        self.metric = ChordRightClick(logical_size)
        self.origins = {}
        self.open = False

    def update(self, contacts, slop_mm):
        active = {c.slot: c for c in contacts if c.active}
        if len(active) != 2:
            self.origins = {}
            self.open = False
            return len(active) > 2
        if set(active) != set(self.origins):
            self.origins = {slot: (c.x, c.y) for slot, c in active.items()}
            self.open = False
        if any(self.metric.distance(c, self.origins[slot]) > slop_mm for slot, c in active.items()):
            self.open = True
        return self.open
