package com.jsh.erp.service.cashier;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.datasource.entities.CashierServiceTimer;
import com.jsh.erp.datasource.mappers.CashierServiceTimerMapper;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.util.Date;
import java.util.HashMap;
import java.util.Map;

@Service
public class CashierServiceTimerService {
    @Resource
    private CashierServiceTimerMapper cashierServiceTimerMapper;

    @Resource
    private CashierSessionService cashierSessionService;

    public Map<String, Object> currentBySessionId(Long sessionId, Long tenantId) throws Exception {
        cashierSessionService.ensureSessionPermission(sessionId, tenantId);
        CashierServiceTimer timer = cashierServiceTimerMapper.selectLatestBySessionId(sessionId, tenantId);
        Map<String, Object> map = new HashMap<>();
        if (timer == null) {
            map.put("timer", null);
            map.put("remainSeconds", 0);
            return map;
        }
        timer = autoFinishIfNeeded(timer, tenantId);
        map.put("timer", timer);
        map.put("remainSeconds", calcRemainSeconds(timer));
        return map;
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public CashierServiceTimer start(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        Long sessionId = obj.getLong("sessionId");
        Integer durationSeconds = obj.getInteger("durationSeconds");
        if (sessionId == null) {
            throw new RuntimeException("参数错误");
        }
        if (durationSeconds == null || durationSeconds <= 0) {
            throw new RuntimeException("请填写服务时长");
        }
        cashierSessionService.ensureSessionPermission(sessionId, tenantId);
        CashierServiceTimer existed = cashierServiceTimerMapper.selectLatestBySessionId(sessionId, tenantId);
        Date now = new Date();
        Date end = new Date(now.getTime() + durationSeconds.longValue() * 1000L);
        if (existed != null && existed.getId() != null) {
            CashierServiceTimer update = new CashierServiceTimer();
            update.setId(existed.getId());
            update.setTenantId(tenantId);
            update.setStatus("RUNNING");
            update.setDurationSeconds(durationSeconds);
            update.setStartTime(now);
            update.setEndTime(end);
            cashierServiceTimerMapper.updateByPrimaryKeySelective(update);
            return cashierServiceTimerMapper.selectByPrimaryKey(existed.getId());
        }
        CashierServiceTimer timer = new CashierServiceTimer();
        timer.setSessionId(sessionId);
        timer.setSeatId(null);
        timer.setStatus("RUNNING");
        timer.setDurationSeconds(durationSeconds);
        timer.setStartTime(now);
        timer.setEndTime(end);
        timer.setTenantId(tenantId);
        timer.setDeleteFlag("0");
        cashierServiceTimerMapper.insertSelective(timer);
        return timer;
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public CashierServiceTimer finish(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        Long sessionId = obj.getLong("sessionId");
        if (sessionId == null) {
            throw new RuntimeException("参数错误");
        }
        cashierSessionService.ensureSessionPermission(sessionId, tenantId);
        CashierServiceTimer existed = cashierServiceTimerMapper.selectLatestBySessionId(sessionId, tenantId);
        if (existed == null || existed.getId() == null) {
            return null;
        }
        CashierServiceTimer update = new CashierServiceTimer();
        update.setId(existed.getId());
        update.setTenantId(tenantId);
        update.setStatus("FINISHED");
        update.setEndTime(new Date());
        cashierServiceTimerMapper.updateByPrimaryKeySelective(update);
        return cashierServiceTimerMapper.selectByPrimaryKey(existed.getId());
    }

    public void ensureServiceFinishedBeforeCheckout(Long sessionId, Long tenantId) throws Exception {
        if (sessionId == null) {
            return;
        }
        CashierServiceTimer timer = cashierServiceTimerMapper.selectLatestBySessionId(sessionId, tenantId);
        if (timer == null) {
            return;
        }
        timer = autoFinishIfNeeded(timer, tenantId);
        if ("RUNNING".equalsIgnoreCase(timer.getStatus()) && calcRemainSeconds(timer) > 0) {
            throw new RuntimeException("服务进行中，倒计时结束后才能结算");
        }
    }

    private CashierServiceTimer autoFinishIfNeeded(CashierServiceTimer timer, Long tenantId) {
        if (timer == null) {
            return null;
        }
        if (timer.getEndTime() == null) {
            return timer;
        }
        if (!"RUNNING".equalsIgnoreCase(timer.getStatus())) {
            return timer;
        }
        if (timer.getEndTime().getTime() > System.currentTimeMillis()) {
            return timer;
        }
        CashierServiceTimer update = new CashierServiceTimer();
        update.setId(timer.getId());
        update.setTenantId(tenantId);
        update.setStatus("FINISHED");
        cashierServiceTimerMapper.updateByPrimaryKeySelective(update);
        timer.setStatus("FINISHED");
        return timer;
    }

    private int calcRemainSeconds(CashierServiceTimer timer) {
        if (timer == null || timer.getEndTime() == null) {
            return 0;
        }
        long diff = timer.getEndTime().getTime() - System.currentTimeMillis();
        if (diff <= 0) {
            return 0;
        }
        long sec = diff / 1000L;
        return sec > Integer.MAX_VALUE ? Integer.MAX_VALUE : (int) sec;
    }
}

