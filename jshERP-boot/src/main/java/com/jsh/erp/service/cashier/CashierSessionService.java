package com.jsh.erp.service.cashier;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.datasource.entities.CashierSession;
import com.jsh.erp.datasource.entities.Seat;
import com.jsh.erp.datasource.mappers.CashierSessionMapper;
import com.jsh.erp.datasource.mappers.SeatMapper;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.util.Date;

@Service
public class CashierSessionService {
    @Resource
    private CashierSessionMapper cashierSessionMapper;

    @Resource
    private SeatMapper seatMapper;

    public CashierSession getCurrentBySeatId(Long seatId, Long tenantId) throws Exception {
        return cashierSessionMapper.selectCurrentBySeatId(seatId, tenantId);
    }

    public CashierSession getById(Long id) throws Exception {
        return cashierSessionMapper.selectByPrimaryKey(id);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public CashierSession openSession(JSONObject obj, Long tenantId, Long operatorUserId, HttpServletRequest request) throws Exception {
        Long seatId = obj.getLong("seatId");
        Long depotId = obj.getLong("depotId");
        Long memberId = obj.getLong("memberId");
        String remark = obj.getString("remark");

        CashierSession existed = cashierSessionMapper.selectCurrentBySeatId(seatId, tenantId);
        if (existed != null) {
            return existed;
        }

        CashierSession session = new CashierSession();
        session.setSeatId(seatId);
        session.setDepotId(depotId);
        session.setMemberId(memberId);
        session.setStatus("OPEN");
        session.setStartTime(new Date());
        session.setOperatorUserId(operatorUserId);
        session.setTenantId(tenantId);
        session.setRemark(remark);
        session.setDeleteFlag("0");
        cashierSessionMapper.insertSelective(session);

        if (seatId != null) {
            Seat seat = new Seat();
            seat.setId(seatId);
            seat.setStatus(1);
            seatMapper.updateByPrimaryKeySelective(seat);
        }

        return session;
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int bindMember(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        Long sessionId = obj.getLong("sessionId");
        Long memberId = obj.getLong("memberId");
        CashierSession session = new CashierSession();
        session.setId(sessionId);
        session.setTenantId(tenantId);
        session.setMemberId(memberId);
        return cashierSessionMapper.updateByPrimaryKeySelective(session);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int closeSession(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        Long sessionId = obj.getLong("sessionId");
        CashierSession db = cashierSessionMapper.selectByPrimaryKey(sessionId);
        if (db == null) {
            return 0;
        }
        CashierSession session = new CashierSession();
        session.setId(sessionId);
        session.setTenantId(tenantId);
        session.setStatus("CLOSED");
        session.setEndTime(new Date());
        int result = cashierSessionMapper.updateByPrimaryKeySelective(session);
        if (result > 0 && db.getSeatId() != null) {
            Seat seat = new Seat();
            seat.setId(db.getSeatId());
            seat.setStatus(0);
            seatMapper.updateByPrimaryKeySelective(seat);
        }
        return result;
    }
}

