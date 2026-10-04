//! The per-id result cache: a redelivered call is answered from here instead of running again.

use std::collections::{HashMap, VecDeque};
use std::time::{Duration, Instant};

/// Results by call id, bounded by count and age.
pub(crate) struct ResultCache {
    map: HashMap<String, (Instant, String)>,
    order: VecDeque<String>,
    capacity: usize,
    ttl: Duration,
}

impl ResultCache {
    pub(crate) fn new(capacity: usize, ttl: Duration) -> ResultCache {
        ResultCache {
            map: HashMap::new(),
            order: VecDeque::new(),
            capacity: capacity.max(1),
            ttl,
        }
    }

    /// The cached result message of `id`, if it is still fresh.
    pub(crate) fn get(&mut self, id: &str) -> Option<String> {
        self.expire(Instant::now());
        self.map.get(id).map(|(_, msg)| msg.clone())
    }

    /// Keep the result message of `id`.
    pub(crate) fn insert(&mut self, id: &str, msg: String) {
        let now = Instant::now();
        self.expire(now);
        if self.map.insert(id.to_string(), (now, msg)).is_none() {
            self.order.push_back(id.to_string());
        }
        while self.order.len() > self.capacity {
            if let Some(old) = self.order.pop_front() {
                self.map.remove(&old);
            }
        }
    }

    fn expire(&mut self, now: Instant) {
        while let Some(front) = self.order.front() {
            let stale = self
                .map
                .get(front)
                .is_none_or(|(at, _)| now.duration_since(*at) > self.ttl);
            if !stale {
                break;
            }
            if let Some(old) = self.order.pop_front() {
                self.map.remove(&old);
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn bounded_by_count_and_age() {
        let mut c = ResultCache::new(2, Duration::from_secs(60));
        c.insert("a", "1".into());
        c.insert("b", "2".into());
        c.insert("c", "3".into());
        assert_eq!(c.get("a"), None);
        assert_eq!(c.get("c").as_deref(), Some("3"));
        let mut c = ResultCache::new(10, Duration::ZERO);
        c.insert("a", "1".into());
        std::thread::sleep(Duration::from_millis(2));
        assert_eq!(c.get("a"), None);
    }
}
